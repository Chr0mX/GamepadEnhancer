using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// Translates raw WM_INPUT buffers (RAWINPUT) into events. Pure and platform independent so it can be unit tested.
    /// RAWINPUTHEADER = { DWORD dwType; DWORD dwSize; HANDLE hDevice; WPARAM wParam; }, so the header is 24 bytes on x64
    /// and 16 on x86; the RAWMOUSE / RAWKEYBOARD payload follows it.
    /// </summary>
    internal static class RawInputParser
    {
        public const int TypeMouse = 0;
        public const int TypeKeyboard = 1;

        private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001, RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
        private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004, RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
        private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010, RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
        private const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040, RI_MOUSE_BUTTON_4_UP = 0x0080;
        private const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100, RI_MOUSE_BUTTON_5_UP = 0x0200;
        private const ushort RI_MOUSE_WHEEL = 0x0400;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
        private const ushort RI_KEY_BREAK = 0x0001, RI_KEY_E0 = 0x0002;

        public static int HeaderSize(int pointerSize) => 8 + 2 * pointerSize;

        /// <summary>
        /// Reads one RAWINPUT buffer and reports it to <paramref name="receiver"/>.
        /// <paramref name="synthetic"/> is true when hDevice is NULL, which is how Windows labels input injected with SendInput.
        /// </summary>
        /// <param name="movementOnly">true: report only relative mouse movement (the mouse-look source); false: report keys, buttons and wheel, never movement.</param>
        public static bool Route(ReadOnlySpan<byte> buffer, int pointerSize, InputCaptureMethod method, IInputEventReceiver receiver, out bool synthetic, bool movementOnly = false)
        {
            synthetic = false;
            int header = HeaderSize(pointerSize);
            if (buffer.Length < header) return false;

            int type = BitConverter.ToInt32(buffer.Slice(0, 4));
            long handle = pointerSize == 8 ? BitConverter.ToInt64(buffer.Slice(8, 8)) : BitConverter.ToInt32(buffer.Slice(8, 4));
            IntPtr device = new IntPtr(handle);
            synthetic = device == IntPtr.Zero;
            ReadOnlySpan<byte> data = buffer.Slice(header);

            if (type == TypeKeyboard)
            {
                if (movementOnly) return true;
                if (data.Length < 10) return false;
                ushort makeCode = BitConverter.ToUInt16(data.Slice(0, 2));
                ushort flags = BitConverter.ToUInt16(data.Slice(2, 2));
                ushort vkey = BitConverter.ToUInt16(data.Slice(6, 2));
                if (IsIgnoredKey(makeCode, flags, vkey)) return true;
                receiver.KeyChanged(method, vkey, (flags & RI_KEY_BREAK) == 0, device);
                return true;
            }

            if (type == TypeMouse)
            {
                if (data.Length < 8) return false;
                if (movementOnly)
                {
                    // RAWMOUSE: usFlags(0) ... lLastX(12) lLastY(16). Absolute devices (touch, remote desktop) are skipped.
                    if (data.Length < 20) return false;
                    ushort moveFlags = BitConverter.ToUInt16(data.Slice(0, 2));
                    if ((moveFlags & MOUSE_MOVE_ABSOLUTE) != 0) return true;
                    int dx = BitConverter.ToInt32(data.Slice(12, 4));
                    int dy = BitConverter.ToInt32(data.Slice(16, 4));
                    if (dx != 0 || dy != 0) receiver.MouseMoved(method, dx, dy);
                    return true;
                }
                ushort buttons = BitConverter.ToUInt16(data.Slice(4, 2));
                if ((buttons & RI_MOUSE_LEFT_BUTTON_DOWN) != 0) receiver.MouseButtonChanged(method, MouseButton.Left, true, device);
                if ((buttons & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) receiver.MouseButtonChanged(method, MouseButton.Right, true, device);
                if ((buttons & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) receiver.MouseButtonChanged(method, MouseButton.Middle, true, device);
                if ((buttons & RI_MOUSE_BUTTON_4_DOWN) != 0) receiver.MouseButtonChanged(method, MouseButton.X1, true, device);
                if ((buttons & RI_MOUSE_BUTTON_5_DOWN) != 0) receiver.MouseButtonChanged(method, MouseButton.X2, true, device);
                if ((buttons & RI_MOUSE_WHEEL) != 0)
                {
                    short wheelDelta = data.Length >= 8 ? BitConverter.ToInt16(data.Slice(6, 2)) : (short)0;
                    receiver.WheelScrolled(method, wheelDelta > 0 ? 1 : wheelDelta < 0 ? -1 : 0);
                }
                if ((buttons & RI_MOUSE_LEFT_BUTTON_UP) != 0) receiver.MouseButtonChanged(method, MouseButton.Left, false, device);
                if ((buttons & RI_MOUSE_RIGHT_BUTTON_UP) != 0) receiver.MouseButtonChanged(method, MouseButton.Right, false, device);
                if ((buttons & RI_MOUSE_MIDDLE_BUTTON_UP) != 0) receiver.MouseButtonChanged(method, MouseButton.Middle, false, device);
                if ((buttons & RI_MOUSE_BUTTON_4_UP) != 0) receiver.MouseButtonChanged(method, MouseButton.X1, false, device);
                if ((buttons & RI_MOUSE_BUTTON_5_UP) != 0) receiver.MouseButtonChanged(method, MouseButton.X2, false, device);
                return true;
            }

            return false;   // HID and other types are not used
        }

        /// <summary>
        /// Raw Input reports extra keyboard events that are not real key presses: VKey 0xFF (overrun / escape sequences),
        /// VKey 0 and the "fake shift" (scan code 0x2A with the E0 prefix) Windows inserts around some NumLock/arrow combos.
        /// </summary>
        public static bool IsIgnoredKey(ushort makeCode, ushort flags, ushort vkey)
            => vkey == 0 || vkey == 0xFF || (makeCode == 0x2A && (flags & RI_KEY_E0) != 0);
    }

    /// <summary>
    /// Raw Input capture backend. A dedicated thread owns a hidden window and registers the generic mouse and keyboard
    /// with <c>RIDEV_INPUTSINK</c> (background capture) and <c>RIDEV_DEVNOTIFY</c> (device arrival/removal). It does NOT
    /// use <c>RIDEV_NOLEGACY</c>, so normal mouse/keyboard messages keep flowing to other windows; nothing is intercepted,
    /// blocked or injected. WM_INPUT buffers are decoded by <see cref="RawInputParser"/>.
    /// </summary>
    internal sealed class RawInputService : IInputCaptureSource
    {
        private const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_INPUT = 0x00FF;
        private const uint RIDEV_REMOVE = 0x00000001, RIDEV_INPUTSINK = 0x00000100, RIDEV_DEVNOTIFY = 0x00002000;
        private const uint RID_INPUT = 0x10000003;
        private const int GIDC_REMOVAL = 2;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
        private const ushort UsagePageGeneric = 0x01, UsageMouse = 0x02, UsageKeyboard = 0x06;

        // Deliberately no RIDEV_NOLEGACY.
        private const uint RegistrationFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;

        private readonly IInputEventReceiver receiver;
        private readonly bool movementOnly;
        private readonly ManualResetEventSlim ready = new(false);
        private Thread thread;
        private IntPtr hwnd;
        private string startError;
        private volatile bool stopping;
        private WndProc windowProc;   // kept alive for the lifetime of the window

        /// <param name="movementOnly">true: a mouse-only source that reports relative movement (for mouse-look) and nothing else;
        /// it never reports faults to the receiver (see <see cref="Failed"/>) so it cannot disturb the active key/button method.</param>
        public RawInputService(IInputEventReceiver receiver, bool movementOnly = false)
        {
            this.receiver = receiver;
            this.movementOnly = movementOnly;
        }

        /// <summary>Movement-only source: raised (from the window thread) if the message loop dies after start.</summary>
        public event Action<string> Failed;

        public InputCaptureMethod Method => InputCaptureMethod.RawInput;


        public bool Start(out string error)
        {
            error = null;
            if (!OperatingSystem.IsWindows()) { error = "Raw Input is only available on Windows."; return false; }
            if (thread != null) return true;

            stopping = false;
            startError = null;
            ready.Reset();
            thread = new Thread(Run) { IsBackground = true, Name = "Raw Input window" };
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(5)))
            {
                error = "Raw Input did not start in time.";
                Stop();
                return false;
            }
            if (startError != null)
            {
                error = startError;
                Stop();
                return false;
            }
            return true;
        }

        public void Stop()
        {
            Thread t = thread;
            if (t == null) return;
            stopping = true;
            IntPtr h = hwnd;
            if (h != IntPtr.Zero) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);   // the window is destroyed on its own thread
            if (t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(3));
            thread = null;
        }

        public void Dispose() => Stop();

        // ── Window thread ────────────────────────────────────────────────────────

        private void Run()
        {
            IntPtr instance = GetModuleHandle(null);
            string className = "VigemRawInput_" + Guid.NewGuid().ToString("N");
            bool classRegistered = false, devicesRegistered = false, loopStarted = false;
            try
            {
                windowProc = WindowProc;
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = windowProc,
                    hInstance = instance,
                    lpszClassName = className
                };
                if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed (" + Marshal.GetLastWin32Error() + ").");
                classRegistered = true;

                // A hidden, never-shown top-level window. RIDEV_INPUTSINK needs a target window.
                hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, className, "ViGEm Raw Input", WS_POPUP,
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed (" + Marshal.GetLastWin32Error() + ").");

                var deviceList = new System.Collections.Generic.List<RAWINPUTDEVICE>
                {
                    new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageMouse, dwFlags = RegistrationFlags, hwndTarget = hwnd }
                };
                if (!movementOnly)
                    deviceList.Add(new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageKeyboard, dwFlags = RegistrationFlags, hwndTarget = hwnd });
                RAWINPUTDEVICE[] devices = deviceList.ToArray();
                if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                    throw new InvalidOperationException("RegisterRawInputDevices failed (" + Marshal.GetLastWin32Error() + ").");
                devicesRegistered = true;

                loopStarted = true;
                ready.Set();
                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                if (!stopping) ReportFault("The Raw Input message loop ended unexpectedly.");
            }
            catch (Exception ex)
            {
                if (!loopStarted) startError = ex.Message;
                else if (!stopping) ReportFault(ex.Message);
            }
            finally
            {
                if (devicesRegistered)
                {
                    // RIDEV_REMOVE requires a NULL target window.
                    var removeList = new System.Collections.Generic.List<RAWINPUTDEVICE>
                    {
                        new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageMouse, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero }
                    };
                    if (!movementOnly)
                        removeList.Add(new RAWINPUTDEVICE { usUsagePage = UsagePageGeneric, usUsage = UsageKeyboard, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero });
                    RAWINPUTDEVICE[] remove = removeList.ToArray();
                    RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
                }
                IntPtr h = hwnd;
                hwnd = IntPtr.Zero;
                if (h != IntPtr.Zero && IsWindow(h)) DestroyWindow(h);
                if (classRegistered) UnregisterClass(className, instance);
                ready.Set();   // never leave Start() waiting
            }
        }

        private void ReportFault(string message)
        {
            if (movementOnly) { try { Failed?.Invoke(message); } catch { } }
            else receiver.CaptureFaulted(Method, message);
        }

        private IntPtr WindowProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_INPUT:
                    try { HandleInput(lParam); } catch { /* a malformed event must not take the thread down */ }
                    break;   // fall through to DefWindowProc so the system can clean up the RAWINPUT data

                case WM_INPUT_DEVICE_CHANGE:
                    if (wParam.ToInt64() == GIDC_REMOVAL && !stopping && !movementOnly) receiver.DeviceRemoved(Method, lParam);
                    return IntPtr.Zero;

                case WM_CLOSE:
                    DestroyWindow(h);
                    return IntPtr.Zero;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProc(h, msg, wParam, lParam);
        }

        private void HandleInput(IntPtr hRawInput)
        {
            if (stopping) return;
            uint headerSize = (uint)RawInputParser.HeaderSize(IntPtr.Size);
            uint size = 0;
            if (GetRawInputDataSize(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0) return;

            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)size);
            try
            {
                uint copied = GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize);
                if (copied == uint.MaxValue || copied == 0) return;
                RawInputParser.Route(new ReadOnlySpan<byte>(buffer, 0, (int)copied), IntPtr.Size, Method, receiver, out _, movementOnly);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        // ── Win32 ────────────────────────────────────────────────────────────────

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
        [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)]
        private static extern uint GetRawInputDataSize(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, [Out] byte[] pData, ref uint pcbSize, uint cbSizeHeader);
    }
}
