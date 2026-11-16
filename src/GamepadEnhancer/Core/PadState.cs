using System;
using System.Threading;

namespace GamepadEnhancer
{
    /// <summary>Which stage of the input pipeline a controller state describes.</summary>
    internal enum PadView
    {
        /// <summary>What the user's own devices ask for, before any Gamepad Enhancer processing (raw key/mouse/network input).</summary>
        Physical,
        /// <summary>After drift, recoil, filtering, mouse look, turbo and disengage, before it is quantized into a report.</summary>
        Processed,
        /// <summary>Exactly what is written to the virtual controller (what the game receives).</summary>
        Final
    }

    /// <summary>The virtual controller layout the visualization draws.</summary>
    internal enum PadFamily { Xbox360, DualShock4 }

    /// <summary>
    /// Immutable controller state in layout-neutral units: sticks -1..1 with +Y UP (the same on every controller family),
    /// triggers 0..1, and a button mask whose bit i is <see cref="ControllerEngine.BindingActions"/>[i]. Immutable so the
    /// output thread can publish it and a UI thread can read it with no lock.
    /// </summary>
    internal sealed class PadState
    {
        public static readonly PadState Neutral = new(0, 0, 0, 0, 0, 0, 0);

        public PadState(double lx, double ly, double rx, double ry, double lt, double rt, int mask)
        {
            Lx = lx; Ly = ly; Rx = rx; Ry = ry; Lt = lt; Rt = rt; Mask = mask;
        }

        public double Lx { get; }
        public double Ly { get; }
        public double Rx { get; }
        public double Ry { get; }
        public double Lt { get; }
        public double Rt { get; }
        public int Mask { get; }

        public bool Has(int actionIndex) => (Mask & (1 << actionIndex)) != 0;

        public bool SameAs(PadState o) =>
            o != null && Mask == o.Mask && Lx == o.Lx && Ly == o.Ly && Rx == o.Rx && Ry == o.Ry && Lt == o.Lt && Rt == o.Rt;
    }

    /// <summary>Conversions between the engine's native values and <see cref="PadState"/> units. Pure.</summary>
    internal static class PadStateMath
    {
        public const int ActionLt = 6, ActionRt = 7, ActionDpadUp = 12, ActionDpadDown = 13, ActionDpadLeft = 14, ActionDpadRight = 15;
        /// <summary>Bit positions of the keyboard-driven left stick directions (see <see cref="ControllerEngine.BindingActions"/>).</summary>
        public const int ActionLeftUp = 18, ActionLeftDown = 19, ActionLeftLeft = 20, ActionLeftRight = 21;

        /// <summary>
        /// Left stick from the four left-stick key bits, as -1..1 with +Y up (the same convention as <see cref="PadState"/>).
        /// Opposite keys cancel; a diagonal is scaled to the unit circle so it is not faster than a straight direction.
        /// </summary>
        public static (double X, double Y) LeftFromMask(int mask)
        {
            int x = ((mask >> ActionLeftRight) & 1) - ((mask >> ActionLeftLeft) & 1);
            int y = ((mask >> ActionLeftUp) & 1) - ((mask >> ActionLeftDown) & 1);
            if (x != 0 && y != 0) return (x * 0.70710678118654752, y * 0.70710678118654752);
            return (x, y);
        }

        public static double Clamp1(double v) => double.IsNaN(v) ? 0.0 : Math.Clamp(v, -1.0, 1.0);

        /// <summary>Stick percent (-100..100) to -1..1.</summary>
        public static double FromPercent(double percent) => Clamp1(percent / 100.0);

        /// <summary>16-bit Xbox axis to -1..1 (the asymmetric short range maps both ends onto +/-1).</summary>
        public static double FromXboxAxis(short v) => v >= 0 ? v / 32767.0 : Clamp1(v / 32768.0);

        /// <summary>8-bit DualShock 4 axis (centre 128) to -1..1.</summary>
        public static double FromDs4Axis(byte v) => Clamp1((v - 128) / 127.0);

        /// <summary>Network-API stick value (-32768..32767, +Y down) to -1..1 on X.</summary>
        public static double FromRemoteX(int v) => Clamp1(v / 32768.0);

        /// <summary>Network-API stick value (+Y down) to -1..1 with +Y up.</summary>
        public static double FromRemoteYUp(int v) => Clamp1(-v / 32768.0);

        public static double FromTriggerByte(byte b) => b / 255.0;

        /// <summary>Network-API trigger value 0..1023 to 0..1.</summary>
        public static double FromRemoteTrigger(int v) => Math.Clamp(v / (double)RemoteControlState.MaxTrigger, 0.0, 1.0);

        /// <summary>The engine's native Y is +down on PS4 and +up on Xbox 360; the state always uses +up.</summary>
        public static double NativeYToUp(bool xbox, double nativeY) => xbox ? nativeY : -nativeY;

        /// <summary>
        /// The button mask the pad actually reports. A DualShock 4 has a single hat switch, so opposing directions cancel
        /// (the engine's DPad() maps up+down or left+right to neither); an Xbox pad reports every direction bit as pressed.
        /// </summary>
        public static int FinalMask(int mask, bool xbox)
        {
            if (xbox) return mask;
            const int vertical = (1 << ActionDpadUp) | (1 << ActionDpadDown);
            const int horizontal = (1 << ActionDpadLeft) | (1 << ActionDpadRight);
            if ((mask & vertical) == vertical) mask &= ~vertical;
            if ((mask & horizontal) == horizontal) mask &= ~horizontal;
            return mask;
        }

        /// <summary>
        /// Moves a displayed stick coordinate toward its target. Moving away from the centre (or sideways) is instant so the
        /// display never lags the real input; only a return toward the centre eases, with time constant <paramref name="tauMs"/>.
        /// </summary>
        public static double Relax(double current, double target, double dtMs, double tauMs)
        {
            if (Math.Abs(target) >= Math.Abs(current) || Math.Sign(target) * Math.Sign(current) < 0 || tauMs <= 0 || dtMs <= 0) return target;
            double a = 1.0 - Math.Exp(-dtMs / tauMs);
            double next = current + (target - current) * a;
            return Math.Abs(next - target) < 0.002 ? target : next;
        }

        /// <summary>Builds the three views for one engine tick. See <see cref="PadView"/> for what each one means.</summary>
        /// <param name="rawMask">Bound keys/buttons physically down, ignoring turbo and disengage, plus remote buttons.</param>
        /// <param name="mask">The mask the engine applied (turbo and disengage included).</param>
        /// <param name="mouseX">Mouse-look contribution in percent, engine-native Y sign.</param>
        /// <param name="processedX">Right stick after drift/recoil/filter/mouse look, percent, engine-native Y sign.</param>
        /// <param name="finalRightX">The value actually written to the right stick X axis, -1..1.</param>
        /// <param name="finalRightY">The value actually written to the right stick Y axis, -1..1, engine-native Y sign.</param>
        /// <param name="padLeftX">The left stick X as last written to the pad (-1..1).</param>
        /// <param name="padLeftY">The left stick Y as last written to the pad (-1..1, +Y up).</param>
        public static (PadState Physical, PadState Processed, PadState Final) Build(
            bool xbox, RemoteFrame rf, int rawMask, int mask,
            double mouseX, double mouseY, double processedX, double processedY,
            double finalRightX, double finalRightY, double padLeftX, double padLeftY)
        {
            // Left stick: a network client's value while it drives it, otherwise the left-stick keys (physical = held, processed = after turbo).
            (double keyRawX, double keyRawY) = LeftFromMask(rawMask);
            (double keyProcX, double keyProcY) = LeftFromMask(mask);
            double rawLx = rf.LeftActive ? FromRemoteX(rf.Lx) : keyRawX;
            double rawLy = rf.LeftActive ? FromRemoteYUp(rf.Ly) : keyRawY;
            double reqLx = rf.LeftActive ? FromRemoteX(rf.Lx) : keyProcX;
            double reqLy = rf.LeftActive ? FromRemoteYUp(rf.Ly) : keyProcY;
            bool remoteRight = rf.RightActive;

            double rawRx = remoteRight ? FromRemoteX(rf.Rx) : FromPercent(mouseX);
            double rawRy = remoteRight ? FromRemoteYUp(rf.Ry) : NativeYToUp(xbox, FromPercent(mouseY));
            double prRx = remoteRight ? FromRemoteX(rf.Rx) : FromPercent(processedX);
            double prRy = remoteRight ? FromRemoteYUp(rf.Ry) : NativeYToUp(xbox, FromPercent(processedY));

            double rawLt = rf.Lt >= 0 ? FromRemoteTrigger(rf.Lt) : ((rawMask >> ActionLt) & 1);
            double rawRt = rf.Rt >= 0 ? FromRemoteTrigger(rf.Rt) : ((rawMask >> ActionRt) & 1);
            double prLt = rf.Lt >= 0 ? FromRemoteTrigger(rf.Lt) : ((mask >> ActionLt) & 1);
            double prRt = rf.Rt >= 0 ? FromRemoteTrigger(rf.Rt) : ((mask >> ActionRt) & 1);
            double fnLt = rf.Lt >= 0 ? FromTriggerByte(RemoteControlState.ToTriggerByte(rf.Lt)) : ((mask >> ActionLt) & 1);
            double fnRt = rf.Rt >= 0 ? FromTriggerByte(RemoteControlState.ToTriggerByte(rf.Rt)) : ((mask >> ActionRt) & 1);

            var physical = new PadState(rawLx, rawLy, rawRx, rawRy, rawLt, rawRt, rawMask);
            var processed = new PadState(reqLx, reqLy, prRx, prRy, prLt, prRt, mask);
            var final = new PadState(padLeftX, padLeftY, finalRightX, NativeYToUp(xbox, finalRightY), fnLt, fnRt, FinalMask(mask, xbox));
            return (physical, processed, final);
        }
    }

    /// <summary>One button edge seen by the monitor.</summary>
    internal readonly struct PadEvent
    {
        public PadEvent(long tickMs, PadView view, int action, bool pressed) { TickMs = tickMs; View = view; Action = action; Pressed = pressed; }
        public long TickMs { get; }
        public PadView View { get; }
        /// <summary>Index into <see cref="ControllerEngine.BindingActions"/>.</summary>
        public int Action { get; }
        public bool Pressed { get; }
    }

    /// <summary>
    /// Lock-free hand-off from the engine thread to any number of UI readers. The engine only ever swaps three references
    /// and appends the occasional button edge to a ring; nothing here waits on a reader, so an open monitor cannot slow
    /// the output. While no reader is registered (<see cref="Enabled"/> is false) the engine skips building snapshots.
    /// </summary>
    internal sealed class PadMonitor
    {
        public const int RingSize = 256;

        private volatile PadState physical = PadState.Neutral;
        private volatile PadState processed = PadState.Neutral;
        private volatile PadState final = PadState.Neutral;
        private readonly PadEvent[] ring = new PadEvent[RingSize];
        private long eventSeq;                  // number of events ever written; the single writer is the engine thread
        private int[] prevMask = new int[3];   // engine thread only
        private long version;
        private long reports;
        private int viewers;
        private volatile PadFamily family = PadFamily.DualShock4;
        private volatile bool connected;
        private volatile bool remote;

        /// <summary>True while at least one monitor view is open.</summary>
        public bool Enabled => Volatile.Read(ref viewers) > 0;
        public void AddViewer() => Interlocked.Increment(ref viewers);
        public void RemoveViewer() { if (Interlocked.Decrement(ref viewers) < 0) Interlocked.Exchange(ref viewers, 0); }

        public PadFamily Family => family;
        public bool Connected => connected;
        public bool IsRemote => remote;
        /// <summary>Changes whenever any view changed; lets a reader skip redrawing an identical state.</summary>
        public long Version => Interlocked.Read(ref version);
        /// <summary>Total reports submitted to the virtual pad since it was created.</summary>
        public long ReportsSent => Interlocked.Read(ref reports);
        /// <summary>Total button edges recorded so far (a reader's cursor for <see cref="CopyEventsSince"/>).</summary>
        public long EventCount => Volatile.Read(ref eventSeq);

        public PadState Get(PadView view) => view switch
        {
            PadView.Physical => physical,
            PadView.Processed => processed,
            _ => final
        };

        /// <summary>Called when a pad is created or torn down (not from the engine thread while it runs).</summary>
        public void Reset(PadFamily newFamily, bool isConnected, bool isRemote)
        {
            family = newFamily;
            connected = isConnected;
            remote = isRemote;
            physical = processed = final = PadState.Neutral;
            prevMask = new int[3];
            Interlocked.Exchange(ref reports, 0);
            Interlocked.Increment(ref version);
        }

        public void NoteReport() => Interlocked.Increment(ref reports);

        /// <summary>Engine thread only.</summary>
        public void Publish(PadState p, PadState q, PadState f, long tickMs)
        {
            bool changed = false;
            if (!p.SameAs(physical)) { physical = p; changed = true; }
            if (!q.SameAs(processed)) { processed = q; changed = true; }
            if (!f.SameAs(final)) { final = f; changed = true; }
            if (!changed) return;

            Edges(PadView.Physical, p.Mask, tickMs);
            Edges(PadView.Processed, q.Mask, tickMs);
            Edges(PadView.Final, f.Mask, tickMs);
            Interlocked.Increment(ref version);
        }

        private void Edges(PadView view, int mask, long tickMs)
        {
            int diff = mask ^ prevMask[(int)view];
            if (diff == 0) return;
            prevMask[(int)view] = mask;
            for (int i = 0; i < 32; i++)
            {
                if ((diff & (1 << i)) == 0) continue;
                long seq = eventSeq;
                ring[(int)(seq % RingSize)] = new PadEvent(tickMs, view, i, (mask & (1 << i)) != 0);
                Volatile.Write(ref eventSeq, seq + 1);   // publish after the slot is written
            }
        }

        /// <summary>
        /// UI thread: copies the events recorded after <paramref name="cursor"/> (oldest first) into <paramref name="into"/> and
        /// advances the cursor. Each reader keeps its own cursor, so several windows can show the history. If a reader fell
        /// further behind than the ring holds, the oldest events are skipped.
        /// </summary>
        public int CopyEventsSince(ref long cursor, PadEvent[] into)
        {
            long end = Volatile.Read(ref eventSeq);
            long start = Math.Max(cursor, end - (RingSize - 8));   // stay clear of the slot the writer may be overwriting
            if (end - start > into.Length) start = end - into.Length;
            int n = 0;
            for (long i = start; i < end; i++) into[n++] = ring[(int)(i % RingSize)];
            cursor = end;
            return n;
        }
    }

    /// <summary>Button names for each layout. Pure.</summary>
    internal static class PadLabels
    {
        private static readonly string[] Xbox =
        {
            "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "View", "Menu", "L3", "R3",
            "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right", "Guide",
            "Touchpad (n/a)", "Left Stick Up", "Left Stick Down", "Left Stick Left", "Left Stick Right"
        };

        private static readonly string[] Ds4 =
        {
            "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "Share", "Options", "L3", "R3",
            "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right", "PS",
            "Touchpad", "Left Stick Up", "Left Stick Down", "Left Stick Left", "Left Stick Right"
        };

        public static string For(PadFamily family, int action)
        {
            string[] t = family == PadFamily.Xbox360 ? Xbox : Ds4;
            return action >= 0 && action < t.Length ? t[action] : "Button " + action;
        }

        public static string FamilyName(PadFamily family) =>
            family == PadFamily.Xbox360 ? "Xbox 360 Controller" : "PlayStation 4 DualShock 4 Controller";

        public static string ViewName(PadView view) => view switch
        {
            PadView.Physical => "Physical input",
            PadView.Processed => "Processed input",
            _ => "Final output (to ViGEm)"
        };
    }
}
