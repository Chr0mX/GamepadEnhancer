using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadEnhancer
{
    /// <summary>
    /// Owns the virtual controller and a dedicated high-rate output thread.
    /// Normal operation reports every 10 ms (as before). With a negative drift value the thread
    /// switches to ~2 ms reports (about 500 per second) and flips the stick value on every report, i.e. hundreds of times
    /// per second.
    /// </summary>
    internal sealed class ControllerEngine : IDisposable, IInputSink
    {
        public const string ActionACross = Inputs.FaceSouth;
        public const string ActionBCircle = Inputs.FaceEast;
        public const string ActionXSquare = Inputs.FaceWest;
        public const string ActionYTriangle = Inputs.FaceNorth;
        public const string ActionLbL1 = Inputs.Lb;
        public const string ActionRbR1 = Inputs.Rb;
        public const string ActionLtL2 = Inputs.Lt;
        public const string ActionRtR2 = Inputs.Rt;
        public const string ActionBackShare = Inputs.Select;
        public const string ActionStartOptions = Inputs.Start;
        public const string ActionLsL3 = Inputs.LsClick;
        public const string ActionRsR3 = Inputs.RsClick;
        public const string ActionDPadUp = Inputs.DpadUp;
        public const string ActionDPadDown = Inputs.DpadDown;
        public const string ActionDPadLeft = Inputs.DpadLeft;
        public const string ActionDPadRight = Inputs.DpadRight;
        /// <summary>Xbox Guide / PlayStation PS button. Appended last so every existing action keeps its bit index.</summary>
        public const string ActionGuidePs = Inputs.Home;
        /// <summary>DualShock 4 touchpad click (no Xbox 360 equivalent; ignored there).</summary>
        public const string ActionTouchpad = Inputs.Touchpad;
        /// <summary>Keyboard-driven left stick directions. They are not buttons: opposite keys cancel, diagonals are normalized.</summary>
        public const string ActionLeftStickUp = Inputs.LsUp;
        public const string ActionLeftStickDown = Inputs.LsDown;
        public const string ActionLeftStickLeft = Inputs.LsLeft;
        public const string ActionLeftStickRight = Inputs.LsRight;

        /// <summary>The engine's buttons in bit order: exactly the canonical physical inputs (see <see cref="Inputs"/>).</summary>
        public static readonly string[] BindingActions = Inputs.Buttons;

        private const double NormalPeriodMs = 10.0;
        /// <summary>An unchanged report is re-sent at least this often so the virtual pad never looks idle/stale.</summary>
        private const long HeartbeatMs = 250;

        private readonly object sync = new();
        private readonly HashSet<string> activeKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> turboHeldSinceMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly MouseLook mouseLook = new();
        private readonly Dictionary<string, long> wheelPulseUntilMs = new(StringComparer.OrdinalIgnoreCase);
        private const long WheelPulseMs = 60;

        private ViGEmClient client;
        private IXboxPad xbox;
        private IDs4Pad ds4;
        private Thread thread;
        private volatile bool running;
        private volatile EngineSettings settings = EngineSettings.Default;

        private bool useXbox;
        private ulong lastReportSig = ulong.MaxValue;   // last report actually sent (axes + buttons)
        private long lastReportMs = -1_000_000;
        private bool submitPending;                      // set by Tick, executed by Loop after the lock is released
        private string lastStatus;

        /// <summary>Physical controller reader for the rear paddles (null = none). Read once per tick, so no extra polling rate.</summary>
        public volatile IPaddleSource PaddleSource;
        /// <summary>Rear paddle processing and keyboard / mouse injection.</summary>
        public PaddleProcessor Paddles { get; } = new();
        /// <summary>Advanced bindings: conditions, toggle, output ownership. The engine only feeds it edges and reads its outputs.</summary>
        internal BindingRuntime Bindings { get; } = new();
        private volatile int lastMask;
        /// <summary>Pad-action bits pressed at the last tick (for live feedback in the UI).</summary>
        public int LastMask => lastMask;

        /// <summary>Controls set over the network (see NetworkApiServer); cleared whenever the pad disconnects.</summary>
        public RemoteControlState Remote { get; } = new();

        public bool IsConnected => running;
        public string ControllerName => ControllerLayouts.ForDevice(Family).Name;
        /// <summary>True while the pad is being sent to another PC instead of a local ViGEm pad.</summary>
        public bool IsRemote { get; private set; }
        public PadFamily Family => useXbox ? PadFamily.Xbox360 : PadFamily.DualShock4;

        /// <summary>
        /// Lock-free snapshots of the physical / processed / final pad state for the live monitor. The engine builds them
        /// only while <see cref="PadMonitor.Enabled"/> is set, and never waits on a reader.
        /// </summary>
        public PadMonitor Snapshots { get; } = new();

        // Left stick and analog trigger values as last written to the pad (-1..1, +Y up); used by the monitor's Final view.
        private double padLx, padLy;

        public EngineSettings Settings
        {
            get => settings;
            set
            {
                settings = value ?? EngineSettings.Default;
                Bindings.SetBindings(settings.AdvancedBindings);
            }
        }

        /// <summary>Raised from the engine thread (or hook thread) whenever the status line changes.</summary>
        public event Action<string> StatusChanged;
        /// <summary>Raised from the engine thread when updating the controller fails; the engine stops itself.</summary>
        public event Action<Exception> Faulted;

        // ── Connection ──────────────────────────────────────────────────────────

        /// <param name="remote">When set, the pad is sent to another PC (Network API) instead of a local ViGEm pad.</param>
        public void Connect(bool xbox360, RemoteTarget remote = null)
        {
            Disconnect();
            try
            {
                useXbox = xbox360;
                if (remote != null)
                {
                    var netPad = new NetworkPad(remote, xbox360);
                    netPad.Link.StatusChanged += text => RaiseStatus("Status: " + text);
                    if (xbox360) xbox = netPad; else ds4 = netPad;
                    netPad.Connect();
                }
                else
                {
                    client = new ViGEmClient();
                    if (xbox360) { xbox = new ViGEmXboxPad(client); xbox.Connect(); }
                    else { ds4 = new ViGEmDs4Pad(client); ds4.Connect(); }
                }
            }
            catch
            {
                ReleaseController();
                throw;
            }

            lock (sync) ResetState();
            IsRemote = remote != null;
            Snapshots.Reset(Family, true, IsRemote);
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "ViGEm output", Priority = ThreadPriority.Normal };
            thread.Start();
            RaiseStatus($"Status: {ControllerName} connected");
        }

        public void Disconnect()
        {
            running = false;
            Thread t = thread;
            thread = null;
            if (t != null && t != Thread.CurrentThread) t.Join(500);
            ReleaseController();
            lock (sync) ResetState();
            Paddles.ReleaseAll();   // nothing a paddle was holding may stay pressed
            Paddles.Flush();
            Bindings.ReleaseAll();  // nor anything a binding was holding (latched toggles included)
            Bindings.Flush(Paddles.Injector);
            lastMask = 0;
            IsRemote = false;
            Snapshots.Reset(Family, false, false);
            lastStatus = null;
        }

        private PaddleReading ReadPaddles()
        {
            try { return PaddleSource?.Read() ?? PaddleReading.None; }
            catch { return PaddleReading.None; }
        }

        private void ReleaseController()
        {
            try { xbox?.Disconnect(); } catch { }
            try { ds4?.Disconnect(); } catch { }
            xbox = null;
            ds4 = null;
            try { client?.Dispose(); } catch { }
            client = null;
        }

        private void ResetState()
        {
            lastReportSig = ulong.MaxValue;   // force the next tick to send a report
            lastReportMs = -1_000_000;
            submitPending = false;
            turboHeldSinceMs.Clear();
            wheelPulseUntilMs.Clear();
            Bindings.ReleaseAll();
            mouseLook.Reset();
            padLx = padLy = 0.0;
            Remote.Clear();
        }

        public void Dispose() => Disconnect();

        // ── Input notifications (called from the hook / UI thread) ──────────────

        public void OnKey(string name, bool isDown)
        {
            if (Paddles.IsInjected(name) || Bindings.IsInjected(name)) return;   // a key a paddle or a binding is pressing itself, not the user's input
            lock (sync)
            {
                if (isDown) activeKeys.Add(name); else activeKeys.Remove(name);
            }
            Bindings.OnEdge(name, isDown, NowMs());
        }

        /// <summary>Mouse button event; <paramref name="button"/> is "Mouse Left", "Mouse Right", "Mouse Middle", "Mouse X1" or "Mouse X2".</summary>
        public void OnMouseButton(string button, bool isDown)
        {
            // Mouse buttons can be bound to pad actions like keys ("Mouse Left", "Mouse X1", ...).
            if (Paddles.IsInjected(button) || Bindings.IsInjected(button)) return;
            lock (sync)
            {
                if (isDown) activeKeys.Add(button); else activeKeys.Remove(button);
            }
            Bindings.OnEdge(button, isDown, NowMs());
        }

        public void OnWheel(int direction)
        {
            lock (sync)
            {
                // A wheel notch has no "up": it acts as a short press of "Wheel Up" / "Wheel Down" for bindings.
                if (direction != 0)
                {
                    string name = direction > 0 ? MouseInputNames.WheelUp : MouseInputNames.WheelDown;
                    activeKeys.Add(name);
                    wheelPulseUntilMs[name] = NowMs() + WheelPulseMs;
                    Bindings.OnEdge(name, true, NowMs());
                }
            }
        }

        /// <summary>Relative mouse movement (counts); only used when mouse-look is enabled.</summary>
        public void OnMouseMove(int dx, int dy)
        {
            if (settings.MouseLookEnabled) mouseLook.Accumulate(dx, dy);
        }

        private void ExpireWheelPulses(long now)
        {
            if (wheelPulseUntilMs.Count == 0) return;
            List<string> done = null;
            foreach (var pair in wheelPulseUntilMs)
                if (now >= pair.Value) (done ??= new List<string>()).Add(pair.Key);
            if (done == null) return;
            foreach (string name in done) { wheelPulseUntilMs.Remove(name); activeKeys.Remove(name); Bindings.OnEdge(name, false, now); }
        }

        private long NowMs() => clock.ElapsedMilliseconds;

        // ── Output thread ───────────────────────────────────────────────────────

        private void Loop()
        {
            bool highRes = false;
            if (OperatingSystem.IsWindows()) { try { highRes = NativeTime.timeBeginPeriod(1) == 0; } catch { } }

            try
            {
                double next = clock.Elapsed.TotalMilliseconds;
                while (running)
                {
                    try
                    {
                        Tick();
                        Paddles.Flush();   // paddle -> key / mouse presses, sent outside the engine lock
                        Bindings.Flush(Paddles.Injector);   // binding key / mouse outputs, likewise
                        if (submitPending)
                        {
                            submitPending = false;
                            // The driver call happens outside the lock so mouse/keyboard hooks never wait on it.
                            IXboxPad x = xbox; IDs4Pad d = ds4;
                            if (useXbox) x?.SubmitReport(); else d?.SubmitReport();
                            Snapshots.NoteReport();
                        }
                    }
                    catch (Exception ex)
                    {
                        running = false;
                        try { Faulted?.Invoke(ex); } catch { }
                        break;
                    }

                    next += NormalPeriodMs;
                    double now = clock.Elapsed.TotalMilliseconds;
                    if (next < now - 50) next = now; // don't try to catch up after a stall

                    // Sleep only (the 1 ms timer resolution makes this accurate enough); no spinning, so no CPU burn.
                    while (running && (now = clock.Elapsed.TotalMilliseconds) < next) Thread.Sleep(1);
                }
            }
            finally
            {
                if (highRes) { try { NativeTime.timeEndPeriod(1); } catch { } }
            }
        }

        /// <summary>
        /// Builds one report: key/mouse/wheel bindings (turbo) and remote buttons; the right stick is mouse look (when on),
        /// replaced by the remote right stick while the network client drives it; the left stick and triggers come from the
        /// network client only. Drift, filtering and recoil are Axiom's job now and no longer exist here.
        /// </summary>
        private void Tick()
        {
            EngineSettings s = settings;
            lock (sync)
            {
                long now = NowMs();
                ExpireWheelPulses(now);

                double nowPrecise = clock.Elapsed.TotalMilliseconds;
                double outX = 0.0, outY = 0.0;               // right stick percent, engine-native Y (+down on PS4, +up on Xbox)
                double mouseLookX = 0.0, mouseLookY = 0.0;   // mouse-look contribution, for the monitor's Physical view
                if (s.MouseLookEnabled)
                {
                    (double mx, double my) = mouseLook.Step(nowPrecise, s.MouseSensitivityX, s.MouseSensitivityY,
                        s.MouseCurve, s.MouseGameDeadZonePercent / 100.0, s.MouseReleaseMs, s.MouseInvertY);
                    if (useXbox) my = -my;   // mouse down is +Y here (PS4 convention); on Xbox +Y is up
                    mouseLookX = mx;
                    mouseLookY = my;
                    outX = Math.Clamp(mx, -100.0, 100.0);
                    outY = Math.Clamp(my, -100.0, 100.0);
                }
                else mouseLook.Reset();
                double processedX = outX, processedY = outY;

                RemoteFrame rf = Remote.Snapshot(now);
                int paddleMask = Paddles.Update(ReadPaddles(), s.Paddles, s.PaddleSourceIndex);
                Bindings.Update(now);
                int baseMask = PressedMask(s, now) | rf.ButtonMask | paddleMask;
                int mask = baseMask | Bindings.PadMask;
                lastMask = mask;
                if (useXbox && xbox != null)
                {
                    short rx = StickMath.PercentToXboxAxis(outX);
                    short rsY = StickMath.PercentToXboxAxis(outY);
                    if (rf.RightActive) { rx = RemoteControlState.ToXboxAxis(rf.Rx); rsY = RemoteControlState.ToXboxAxisY(rf.Ry); }
                    xbox.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                    xbox.SetAxisValue(Xbox360Axis.RightThumbY, rsY);
                    ApplyXboxBindings(mask);
                    byte bl = Bindings.TriggerLevel(true), br = Bindings.TriggerLevel(false);   // binding triggers can be partial pulls
                    if (bl > 0 && !Has(baseMask, ActionLtL2)) xbox.SetSliderValue(Xbox360Slider.LeftTrigger, bl);
                    if (br > 0 && !Has(baseMask, ActionRtR2)) xbox.SetSliderValue(Xbox360Slider.RightTrigger, br);
                    ApplyXboxLeftAndTriggers(rf, mask);
                    MarkReport(rx, rsY, mask, now, rf.Signature);
                    if (Snapshots.Enabled)
                        PublishPad(s, now, rf, mask, mouseLookX, mouseLookY, processedX, processedY,
                            PadStateMath.FromXboxAxis(rx), PadStateMath.FromXboxAxis(rsY));
                }
                else if (!useXbox && ds4 != null)
                {
                    byte rx = StickMath.PercentToDs4Axis(outX);
                    byte rsY = StickMath.PercentToDs4Axis(outY);
                    if (rf.RightActive) { rx = RemoteControlState.ToDs4Axis(rf.Rx); rsY = RemoteControlState.ToDs4Axis(rf.Ry); }
                    ds4.SetAxisValue(DualShock4Axis.RightThumbX, rx);
                    ds4.SetAxisValue(DualShock4Axis.RightThumbY, rsY);
                    ApplyDs4Bindings(mask);
                    byte bl = Bindings.TriggerLevel(true), br = Bindings.TriggerLevel(false);
                    if (bl > 0 && !Has(baseMask, ActionLtL2)) ds4.SetSliderValue(DualShock4Slider.LeftTrigger, bl);
                    if (br > 0 && !Has(baseMask, ActionRtR2)) ds4.SetSliderValue(DualShock4Slider.RightTrigger, br);
                    ApplyDs4LeftAndTriggers(rf, mask);
                    MarkReport(rx, rsY, mask, now, rf.Signature);
                    if (Snapshots.Enabled)
                        PublishPad(s, now, rf, mask, mouseLookX, mouseLookY, processedX, processedY,
                            PadStateMath.FromDs4Axis(rx), PadStateMath.FromDs4Axis(rsY));
                }

                RaiseStatus($"Status: {ControllerName} connected");
            }
        }

        /// <summary>
        /// Decides whether this tick's report must be sent: only when axes/buttons changed, plus a heartbeat.
        /// (A static state then costs a few driver calls per second instead of one per tick.)
        /// </summary>
        private void MarkReport(int rx, int ry, int mask, long now, ulong extra = 0)
        {
            ulong sig = ((ushort)rx | ((ulong)(ushort)ry << 16) | ((ulong)(uint)mask << 32)) ^ extra;
            if (sig == lastReportSig && now - lastReportMs < HeartbeatMs) return;
            lastReportSig = sig;
            lastReportMs = now;
            submitPending = true;
        }

        /// <summary>Bit i is set when BindingActions[i] is physically held, ignoring turbo and remote control. Under the lock.</summary>
        private int RawMask(EngineSettings s)
        {
            int mask = 0;
            for (int i = 0; i < BindingActions.Length; i++)
            {
                if (s.Bindings.TryGetValue(BindingActions[i], out BindingSetting b)
                    && !string.Equals(b.Key, "None", StringComparison.OrdinalIgnoreCase)
                    && !Bindings.Suppresses(b.Key)
                    && activeKeys.Contains(b.Key))
                    mask |= 1 << i;
            }
            return mask;
        }

        /// <summary>
        /// Hands the three views (physical / processed / final) to the monitor. Only called while a monitor is open, under
        /// the engine lock, and only swaps references, so it cannot wait on the UI.
        /// </summary>
        private void PublishPad(EngineSettings s, long now, RemoteFrame rf, int mask,
            double mouseX, double mouseY, double processedX, double processedY, double finalRightX, double finalRightY)
        {
            bool xb = useXbox;
            int rawMask = RawMask(s) | rf.ButtonMask | Paddles.LastPadMask;
            var (physical, processed, final) = PadStateMath.Build(xb, rf, rawMask, mask,
                mouseX, mouseY, processedX, processedY, finalRightX, finalRightY, padLx, padLy);
            Snapshots.Publish(physical, processed, final, now);
        }

        private void RaiseStatus(string text)
        {
            if (text == lastStatus) return;
            lastStatus = text;
            StatusChanged?.Invoke(text);
        }

        // ── Bindings ────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, int> ActionBit = BuildActionBits();

        private static Dictionary<string, int> BuildActionBits()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < BindingActions.Length; i++) d[BindingActions[i]] = i;
            return d;
        }

        /// <summary>Bit i is set when BindingActions[i] is pressed (turbo timing included).</summary>
        private int PressedMask(EngineSettings s, long now)
        {
            int mask = 0;
            for (int i = 0; i < BindingActions.Length; i++)
                if (Pressed(s, BindingActions[i], now)) mask |= 1 << i;
            return mask;
        }

        private static bool Has(int mask, string action) => (mask & (1 << ActionBit[action])) != 0;

        private void ApplyXboxBindings(int m)
        {
            xbox.SetButtonState(Xbox360Button.A, Has(m, ActionACross));
            xbox.SetButtonState(Xbox360Button.B, Has(m, ActionBCircle));
            xbox.SetButtonState(Xbox360Button.X, Has(m, ActionXSquare));
            xbox.SetButtonState(Xbox360Button.Y, Has(m, ActionYTriangle));
            xbox.SetButtonState(Xbox360Button.LeftShoulder, Has(m, ActionLbL1));
            xbox.SetButtonState(Xbox360Button.RightShoulder, Has(m, ActionRbR1));
            xbox.SetButtonState(Xbox360Button.Back, Has(m, ActionBackShare));
            xbox.SetButtonState(Xbox360Button.Start, Has(m, ActionStartOptions));
            xbox.SetButtonState(Xbox360Button.LeftThumb, Has(m, ActionLsL3));
            xbox.SetButtonState(Xbox360Button.RightThumb, Has(m, ActionRsR3));
            xbox.SetButtonState(Xbox360Button.Up, Has(m, ActionDPadUp));
            xbox.SetButtonState(Xbox360Button.Down, Has(m, ActionDPadDown));
            xbox.SetButtonState(Xbox360Button.Left, Has(m, ActionDPadLeft));
            xbox.SetButtonState(Xbox360Button.Right, Has(m, ActionDPadRight));
            xbox.SetButtonState(Xbox360Button.Guide, Has(m, ActionGuidePs));
            xbox.SetSliderValue(Xbox360Slider.LeftTrigger, Has(m, ActionLtL2) ? byte.MaxValue : (byte)0);
            xbox.SetSliderValue(Xbox360Slider.RightTrigger, Has(m, ActionRtR2) ? byte.MaxValue : (byte)0);
        }

        private void ApplyDs4Bindings(int m)
        {
            ds4.SetButtonState(DualShock4Button.Cross, Has(m, ActionACross));
            ds4.SetButtonState(DualShock4Button.Circle, Has(m, ActionBCircle));
            ds4.SetButtonState(DualShock4Button.Square, Has(m, ActionXSquare));
            ds4.SetButtonState(DualShock4Button.Triangle, Has(m, ActionYTriangle));
            ds4.SetButtonState(DualShock4Button.ShoulderLeft, Has(m, ActionLbL1));
            ds4.SetButtonState(DualShock4Button.ShoulderRight, Has(m, ActionRbR1));
            ds4.SetButtonState(DualShock4Button.TriggerLeft, Has(m, ActionLtL2));
            ds4.SetButtonState(DualShock4Button.TriggerRight, Has(m, ActionRtR2));
            ds4.SetButtonState(DualShock4Button.Share, Has(m, ActionBackShare));
            ds4.SetButtonState(DualShock4Button.Options, Has(m, ActionStartOptions));
            ds4.SetButtonState(DualShock4Button.ThumbLeft, Has(m, ActionLsL3));
            ds4.SetButtonState(DualShock4Button.ThumbRight, Has(m, ActionRsR3));
            ds4.SetSliderValue(DualShock4Slider.LeftTrigger, Has(m, ActionLtL2) ? byte.MaxValue : (byte)0);
            ds4.SetSliderValue(DualShock4Slider.RightTrigger, Has(m, ActionRtR2) ? byte.MaxValue : (byte)0);
            ds4.SetDPadDirection(DPad(m));
            ds4.SetButtonState(DualShock4SpecialButton.Ps, Has(m, ActionGuidePs));
            ds4.SetButtonState(DualShock4SpecialButton.Touchpad, Has(m, ActionTouchpad));
        }

        /// <summary>
        /// Left stick and analog triggers (the right stick and buttons are merged in Tick). The left stick is the network
        /// client's value while it drives it, otherwise the left-stick keys. Under the lock.
        /// </summary>
        private void ApplyXboxLeftAndTriggers(RemoteFrame rf, int mask)
        {
            short lx, ly;
            if (rf.LeftActive) { lx = RemoteControlState.ToXboxAxis(rf.Lx); ly = RemoteControlState.ToXboxAxisY(rf.Ly); }
            else
            {
                (double kx, double ky) = PadStateMath.LeftFromMask(mask);   // +Y up, which is also Xbox's convention
                lx = StickMath.PercentToXboxAxis(kx * 100.0);
                ly = StickMath.PercentToXboxAxis(ky * 100.0);
            }
            xbox.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
            xbox.SetAxisValue(Xbox360Axis.LeftThumbY, ly);
            padLx = PadStateMath.FromXboxAxis(lx);
            padLy = PadStateMath.FromXboxAxis(ly);
            if (rf.Lt >= 0) xbox.SetSliderValue(Xbox360Slider.LeftTrigger, RemoteControlState.ToTriggerByte(rf.Lt));
            if (rf.Rt >= 0) xbox.SetSliderValue(Xbox360Slider.RightTrigger, RemoteControlState.ToTriggerByte(rf.Rt));
        }

        private void ApplyDs4LeftAndTriggers(RemoteFrame rf, int mask)
        {
            byte lx, ly;
            if (rf.LeftActive) { lx = RemoteControlState.ToDs4Axis(rf.Lx); ly = RemoteControlState.ToDs4Axis(rf.Ly); }
            else
            {
                (double kx, double ky) = PadStateMath.LeftFromMask(mask);   // +Y up; the DS4 axis counts down
                lx = StickMath.PercentToDs4Axis(kx * 100.0);
                ly = StickMath.PercentToDs4Axis(-ky * 100.0);
            }
            ds4.SetAxisValue(DualShock4Axis.LeftThumbX, lx);
            ds4.SetAxisValue(DualShock4Axis.LeftThumbY, ly);
            padLx = PadStateMath.FromDs4Axis(lx);
            padLy = -PadStateMath.FromDs4Axis(ly);
            if (rf.Lt >= 0) ds4.SetSliderValue(DualShock4Slider.LeftTrigger, RemoteControlState.ToTriggerByte(rf.Lt));
            if (rf.Rt >= 0) ds4.SetSliderValue(DualShock4Slider.RightTrigger, RemoteControlState.ToTriggerByte(rf.Rt));
        }

        private static DualShock4DPadDirection DPad(int m)
        {
            bool up = Has(m, ActionDPadUp);
            bool down = Has(m, ActionDPadDown);
            bool left = Has(m, ActionDPadLeft);
            bool right = Has(m, ActionDPadRight);

            if (up && right && !down && !left) return DualShock4DPadDirection.Northeast;
            if (up && left && !down && !right) return DualShock4DPadDirection.Northwest;
            if (down && right && !up && !left) return DualShock4DPadDirection.Southeast;
            if (down && left && !up && !right) return DualShock4DPadDirection.Southwest;
            if (up && !down) return DualShock4DPadDirection.North;
            if (down && !up) return DualShock4DPadDirection.South;
            if (left && !right) return DualShock4DPadDirection.West;
            if (right && !left) return DualShock4DPadDirection.East;
            return DualShock4DPadDirection.None;
        }

        private bool Pressed(EngineSettings s, string action, long nowMs)
        {
            if (!s.Bindings.TryGetValue(action, out BindingSetting b)
                || string.Equals(b.Key, "None", StringComparison.OrdinalIgnoreCase))
            {
                turboHeldSinceMs.Remove(action);
                return false;
            }
            if (Bindings.Suppresses(b.Key)) { turboHeldSinceMs.Remove(action); return false; }   // a manual binding owns this input
            if (!activeKeys.Contains(b.Key)) { turboHeldSinceMs.Remove(action); return false; }
            if (!b.Turbo) { turboHeldSinceMs.Remove(action); return true; }

            if (!turboHeldSinceMs.TryGetValue(action, out long since))
            {
                since = nowMs;
                turboHeldSinceMs[action] = since;
            }
            double cycleMs = 1000.0 / Math.Max(1, b.TurboHz);
            return ((nowMs - since) % cycleMs) < (cycleMs / 2.0);
        }

        private static class NativeTime
        {
            [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint ms);
            [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint ms);
        }
    }
}
