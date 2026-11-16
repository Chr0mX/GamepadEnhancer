using System;

namespace GamepadEnhancer
{
    /// <summary>One consistent view of the remote (network) controls at a moment in time.</summary>
    internal readonly struct RemoteFrame
    {
        public RemoteFrame(bool leftActive, bool rightActive, int lx, int ly, int rx, int ry, int lt, int rt, int buttonMask)
        { LeftActive = leftActive; RightActive = rightActive; Lx = lx; Ly = ly; Rx = rx; Ry = ry; Lt = lt; Rt = rt; ButtonMask = buttonMask; }

        public bool LeftActive { get; }
        public bool RightActive { get; }
        /// <summary>Stick values -32768..32767, +Y is down (MAKXD convention, the same on every controller family).</summary>
        public int Lx { get; }
        public int Ly { get; }
        public int Rx { get; }
        public int Ry { get; }
        /// <summary>Trigger value 0..1023, or -1 when the remote is not driving that trigger.</summary>
        public int Lt { get; }
        public int Rt { get; }
        /// <summary>Bit i = ControllerEngine.BindingActions[i] pressed remotely.</summary>
        public int ButtonMask { get; }

        public bool Any => LeftActive || RightActive || Lt >= 0 || Rt >= 0 || ButtonMask != 0;

        /// <summary>Cheap value that changes whenever anything in the frame changes (used by the report change detector).</summary>
        public ulong Signature =>
            ((ulong)(ushort)Lx * 0x9E3779B97F4A7C15UL) ^ ((ulong)(ushort)Ly * 0xC2B2AE3D27D4EB4FUL)
            ^ ((ulong)(uint)(Lt + 1) * 0x165667B19E3779F9UL) ^ ((ulong)(uint)(Rt + 1) * 0x27D4EB2F165667C5UL)
            ^ (LeftActive ? 0xA5A5A5A5UL : 0UL) ^ (RightActive ? 0x5A5A5A5AUL : 0UL)
            ^ ((ulong)(ushort)Rx << 7) ^ ((ulong)(ushort)Ry << 23);
    }

    /// <summary>
    /// Controls set over the network (MAKCU-style canonical ids). Thread-safe; values can carry a hold time after which
    /// they are released and the engine's own behavior (drift, recoil, bindings) takes over again.
    /// Ids: 0-9 buttons (A B X Y LB RB Back Start LS RS), 10 LT, 11 RT, 12 LX, 13 LY, 14 RX, 15 RY, 16-19 D-pad Up/Down/Left/Right.
    /// </summary>
    internal sealed class RemoteControlState
    {
        public const int IdCount = 20;
        public const int IdLeftTrigger = 10, IdRightTrigger = 11, IdLeftX = 12, IdLeftY = 13, IdRightX = 14, IdRightY = 15;
        public const int MaxTrigger = 1023;

        // Remote button id -> bit in ControllerEngine.BindingActions order
        // (A0 B1 X2 Y3 LB4 RB5 LT6 RT7 Back8 Start9 LS10 RS11 Up12 Down13 Left14 Right15).
        private static readonly int[] ButtonBit = { 0, 1, 2, 3, 4, 5, 8, 9, 10, 11 };
        private static readonly int[] DPadBit = { 12, 13, 14, 15 };

        private readonly object sync = new();
        private readonly int[] value = new int[IdCount];
        private readonly bool[] active = new bool[IdCount];
        private readonly long[] expiresAt = new long[IdCount];   // 0 = no expiry

        public static bool IsValidId(int id) => id >= 0 && id < IdCount;

        /// <summary>Sets one control. Buttons: value 0 releases, any other value presses. Returns false for an unknown id.</summary>
        public bool Set(int id, int val, int holdMs, long nowMs)
        {
            if (!IsValidId(id)) return false;
            lock (sync)
            {
                if (id == IdLeftTrigger || id == IdRightTrigger) val = Math.Clamp(val, 0, MaxTrigger);
                else if (id >= IdLeftX && id <= IdRightY) val = Math.Clamp(val, short.MinValue, short.MaxValue);
                else val = val != 0 ? 1 : 0;

                bool isButton = id < IdLeftTrigger || id > IdRightY;
                value[id] = val;
                active[id] = !(isButton && val == 0);
                expiresAt[id] = active[id] && holdMs > 0 ? nowMs + Math.Min(holdMs, 600_000) : 0;
                return true;
            }
        }

        /// <summary>Drops every remote control at once (also used when the client disconnects).</summary>
        public void Clear()
        {
            lock (sync) { Array.Clear(active); Array.Clear(value); Array.Clear(expiresAt); }
        }

        public RemoteFrame Snapshot(long nowMs)
        {
            lock (sync)
            {
                for (int i = 0; i < IdCount; i++)
                    if (active[i] && expiresAt[i] != 0 && nowMs >= expiresAt[i]) { active[i] = false; value[i] = 0; expiresAt[i] = 0; }

                int mask = 0;
                for (int i = 0; i < ButtonBit.Length; i++) if (active[i]) mask |= 1 << ButtonBit[i];
                for (int i = 0; i < DPadBit.Length; i++) if (active[16 + i]) mask |= 1 << DPadBit[i];

                bool left = active[IdLeftX] || active[IdLeftY];
                bool right = active[IdRightX] || active[IdRightY];
                return new RemoteFrame(left, right,
                    active[IdLeftX] ? value[IdLeftX] : 0, active[IdLeftY] ? value[IdLeftY] : 0,
                    active[IdRightX] ? value[IdRightX] : 0, active[IdRightY] ? value[IdRightY] : 0,
                    active[IdLeftTrigger] ? value[IdLeftTrigger] : -1, active[IdRightTrigger] ? value[IdRightTrigger] : -1,
                    mask);
            }
        }

        // ── Value conversion (+Y is down on every family, so Xbox 360 negates Y) ──────────────

        public static byte ToDs4Axis(int v) => (byte)Math.Clamp((v + 32768) >> 8, 0, 255);
        public static short ToXboxAxis(int v) => (short)Math.Clamp(v, short.MinValue, short.MaxValue);
        public static short ToXboxAxisY(int v) => (short)Math.Clamp(-(long)v, short.MinValue, short.MaxValue);
        public static byte ToTriggerByte(int v) => (byte)Math.Clamp((v * 255 + MaxTrigger / 2) / MaxTrigger, 0, 255);
    }
}
