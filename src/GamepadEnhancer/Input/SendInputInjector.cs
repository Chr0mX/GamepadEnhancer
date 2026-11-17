using System;
using System.Runtime.InteropServices;

namespace GamepadEnhancer
{
    /// <summary>
    /// Presses and releases keyboard keys and mouse buttons with SendInput, for paddles that are mapped to them. One call per press
    /// edge and one per release edge (never repeated). Only the names used in profiles are accepted; anything else is ignored.
    /// </summary>
    internal sealed class SendInputInjector : IInputInjector
    {
        private const uint InputMouse = 0, InputKeyboard = 1;
        private const uint KeyEventExtended = 0x0001, KeyEventKeyUp = 0x0002, KeyEventScanCode = 0x0008;
        private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseRightDown = 0x0008, MouseRightUp = 0x0010,
            MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040, MouseXDown = 0x0080, MouseXUp = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInputData { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInputData { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInputData mi;
            [FieldOffset(0)] public KeyboardInputData ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        public void Send(InjectedInput input, bool down)
        {
            if (!OperatingSystem.IsWindows()) return;
            Input item = default;
            if (input.Kind == PaddleTargetKind.Key)
            {
                int[] vks = KeyNames.ToVirtualKeys(input.Name);
                if (vks.Length == 0) return;
                uint vk = (uint)vks[0];
                uint flags = KeyEventScanCode | (down ? 0u : KeyEventKeyUp);
                if (IsExtended(vk)) flags |= KeyEventExtended;
                item.type = InputKeyboard;
                item.u.ki = new KeyboardInputData { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags };
            }
            else if (input.Kind == PaddleTargetKind.MouseButton)
            {
                if (!TryMouse(input.Name, down, out uint flags, out uint data)) return;
                item.type = InputMouse;
                item.u.mi = new MouseInputData { dwFlags = flags, mouseData = data };
            }
            else return;
            SendInput(1, new[] { item }, Marshal.SizeOf<Input>());
        }

        private static bool IsExtended(uint vk) => (vk >= 0x21 && vk <= 0x28) || vk == 0x2C || vk == 0x2D || vk == 0x2E || vk == 0x6F || vk == 0x90;   // PgUp..Down, Print Screen, Insert, Delete, Num /, Num Lock

        private static bool TryMouse(string name, bool down, out uint flags, out uint data)
        {
            data = 0;
            switch (name)
            {
                case MouseInputNames.Left: flags = down ? MouseLeftDown : MouseLeftUp; return true;
                case MouseInputNames.Right: flags = down ? MouseRightDown : MouseRightUp; return true;
                case MouseInputNames.Middle: flags = down ? MouseMiddleDown : MouseMiddleUp; return true;
                case MouseInputNames.X1: flags = down ? MouseXDown : MouseXUp; data = 1; return true;
                case MouseInputNames.X2: flags = down ? MouseXDown : MouseXUp; data = 2; return true;
                default: flags = 0; return false;
            }
        }
    }
}
