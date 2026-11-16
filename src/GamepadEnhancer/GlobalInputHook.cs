using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamepadEnhancer
{
    /// <summary>
    /// Windows low-level keyboard + mouse hooks. Must be started on a thread that pumps messages
    /// (the Avalonia UI thread does). On other platforms Start() is a no-op returning false.
    /// </summary>
    internal sealed class GlobalInputHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int LLMHF_INJECTED = 0x01;
        private const int LLKHF_INJECTED = 0x10;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly NativeMethods.HookProc _mouseProc;
        private readonly NativeMethods.HookProc _keyboardProc;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private IntPtr _keyboardHookHandle = IntPtr.Zero;
        private bool _disposed;

        public event Action<bool> LeftButtonStateChanged;
        public event Action<bool> RightButtonStateChanged;
        public event Action<bool> MiddleButtonStateChanged;
        /// <summary>Which extra button (1 = X1, 2 = X2) and whether it is down.</summary>
        public event Action<int, bool> XButtonStateChanged;
        /// <summary>Wheel notch: positive = up, negative = down.</summary>
        public event Action<int> WheelScrolled;
        /// <summary>The block toggle / panic hotkey went down (never repeats while held). Raised on the hook thread.</summary>
        public event Action HotkeyPressed;

        /// <summary>When set and active, mapped physical input is swallowed after it has been reported (see <see cref="BlockPolicy"/>).</summary>
        public volatile BlockPolicy Policy;
        private bool hotkeyHeld;
        /// <summary>Virtual-key code and down/up state.</summary>
        public event Action<int, bool> KeyStateChanged;

        public GlobalInputHook()
        {
            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
        }

        public bool Start()
        {
            if (!OperatingSystem.IsWindows()) return false;
            if (_mouseHookHandle != IntPtr.Zero || _keyboardHookHandle != IntPtr.Zero) return true;

            using Process process = Process.GetCurrentProcess();
            using ProcessModule module = process.MainModule;
            IntPtr moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);

            _mouseHookHandle = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
            _keyboardHookHandle = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

            if (_mouseHookHandle == IntPtr.Zero || _keyboardHookHandle == IntPtr.Zero)
            {
                Stop();
                throw new InvalidOperationException("Failed to install global input hooks.");
            }
            return true;
        }

        public void Stop()
        {
            Policy?.Reset();
            hotkeyHeld = false;
            if (!OperatingSystem.IsWindows()) return;
            if (_mouseHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
                _keyboardHookHandle = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                BlockPolicy policy = Policy;

                // Movement is by far the most frequent event: do nothing unless mouse-look is blocking it.
                if (message == WM_MOUSEMOVE)
                {
                    if (policy != null && policy.BlocksMouseMove
                        && policy.DecideMouseMove((Marshal.ReadInt32(lParam, 12) & LLMHF_INJECTED) != 0))
                        return (IntPtr)1;
                    return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                }

                // MSLLHOOKSTRUCT: pt (0, 8 bytes), mouseData (8), flags (12)
                int mouseData = Marshal.ReadInt32(lParam, 8);
                bool injected = (Marshal.ReadInt32(lParam, 12) & LLMHF_INJECTED) != 0;
                bool block = false;
                switch (message)
                {
                    case WM_LBUTTONDOWN: LeftButtonStateChanged?.Invoke(true); block = Decide(policy, MouseInputNames.Left, true, injected); break;
                    case WM_LBUTTONUP: LeftButtonStateChanged?.Invoke(false); block = Decide(policy, MouseInputNames.Left, false, injected); break;
                    case WM_RBUTTONDOWN: RightButtonStateChanged?.Invoke(true); block = Decide(policy, MouseInputNames.Right, true, injected); break;
                    case WM_RBUTTONUP: RightButtonStateChanged?.Invoke(false); block = Decide(policy, MouseInputNames.Right, false, injected); break;
                    case WM_MBUTTONDOWN: MiddleButtonStateChanged?.Invoke(true); block = Decide(policy, MouseInputNames.Middle, true, injected); break;
                    case WM_MBUTTONUP: MiddleButtonStateChanged?.Invoke(false); block = Decide(policy, MouseInputNames.Middle, false, injected); break;
                    case WM_XBUTTONDOWN:
                    case WM_XBUTTONUP:
                    {
                        bool down = message == WM_XBUTTONDOWN;
                        int which = (mouseData >> 16) & 0xFFFF;   // 1 = X1, 2 = X2
                        if (which == 1 || which == 2)
                        {
                            XButtonStateChanged?.Invoke(which, down);
                            block = Decide(policy, which == 1 ? MouseInputNames.X1 : MouseInputNames.X2, down, injected);
                        }
                        break;
                    }
                    case WM_MOUSEWHEEL:
                    {
                        int delta = (short)((mouseData >> 16) & 0xFFFF);
                        int direction = delta > 0 ? 1 : delta < 0 ? -1 : 0;
                        WheelScrolled?.Invoke(direction);
                        block = direction != 0 && policy != null && policy.DecideWheel(direction > 0, injected);
                        break;
                    }
                }
                if (block) return (IntPtr)1;
            }
            return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        private static bool Decide(BlockPolicy policy, string button, bool isDown, bool injected)
            => policy != null && policy.DecideMouseButton(button, isDown, injected);

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                // KBDLLHOOKSTRUCT: vkCode (0), scanCode (4), flags (8)
                int vkCode = Marshal.ReadInt32(lParam);
                bool injected = (Marshal.ReadInt32(lParam, 8) & LLKHF_INJECTED) != 0;
                bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
                if (down || up)
                {
                    KeyStateChanged?.Invoke(vkCode, down);

                    BlockPolicy policy = Policy;
                    if (policy != null)
                    {
                        if (!injected && policy.IsHotkey(vkCode))
                        {
                            if (down && !hotkeyHeld) { hotkeyHeld = true; HotkeyPressed?.Invoke(); }
                            else if (up) hotkeyHeld = false;
                        }
                        if (policy.DecideKey(vkCode, down, injected)) return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private static class NativeMethods
        {
            internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}
