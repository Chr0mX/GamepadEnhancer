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
        private const int WM_MOUSEWHEEL = 0x020A;
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
        public event Action WheelScrolled;
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
                switch (wParam.ToInt32())
                {
                    case WM_LBUTTONDOWN: LeftButtonStateChanged?.Invoke(true); break;
                    case WM_LBUTTONUP: LeftButtonStateChanged?.Invoke(false); break;
                    case WM_RBUTTONDOWN: RightButtonStateChanged?.Invoke(true); break;
                    case WM_RBUTTONUP: RightButtonStateChanged?.Invoke(false); break;
                    case WM_MBUTTONDOWN: MiddleButtonStateChanged?.Invoke(true); break;
                    case WM_MBUTTONUP: MiddleButtonStateChanged?.Invoke(false); break;
                    case WM_MOUSEWHEEL: WheelScrolled?.Invoke(); break;
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN) KeyStateChanged?.Invoke(vkCode, true);
                else if (message == WM_KEYUP || message == WM_SYSKEYUP) KeyStateChanged?.Invoke(vkCode, false);
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
