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
        public const string ActionACross = "A / Cross";
        public const string ActionBCircle = "B / Circle";
        public const string ActionXSquare = "X / Square";
        public const string ActionYTriangle = "Y / Triangle";
        public const string ActionLbL1 = "LB / L1";
        public const string ActionRbR1 = "RB / R1";
        public const string ActionLtL2 = "LT / L2";
        public const string ActionRtR2 = "RT / R2";
        public const string ActionBackShare = "Back / Share";
        public const string ActionStartOptions = "Start / Options";
        public const string ActionLsL3 = "LS / L3";
        public const string ActionRsR3 = "RS / R3";
        public const string ActionDPadUp = "DPad Up";
        public const string ActionDPadDown = "DPad Down";
        public const string ActionDPadLeft = "DPad Left";
        public const string ActionDPadRight = "DPad Right";

        public static readonly string[] BindingActions =
        {
            ActionACross, ActionBCircle, ActionXSquare, ActionYTriangle,
            ActionLbL1, ActionRbR1, ActionLtL2, ActionRtR2,
            ActionBackShare, ActionStartOptions, ActionLsL3, ActionRsR3,
            ActionDPadUp, ActionDPadDown, ActionDPadLeft, ActionDPadRight
        };

        private const double NormalPeriodMs = 10.0;
        /// <summary>Fast loop while filtering is active: ~500 reports/s (each flip of RC-style is still one report).</summary>
        private const double JitterPeriodMs = 2.0;   // default of EngineSettings.FilterPeriodMs
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
        private bool disengaged;
        private long disengagedUntilMs;
        private bool recoilTriggerDown;
        private long recoilStartAtMs;
        private bool jitterLatched;    // RS filtering button held (Hold) or toggled on (Toggle)
        private long jitterChangedMs;
        private int jitterUpPolls;
        private ulong lastReportSig = ulong.MaxValue;   // last report actually sent (axes + buttons)
        private long lastReportMs = -1_000_000;
        private bool submitPending;                      // set by Tick, executed by Loop after the lock is released
        private readonly LowPassFilter lowPass = new();
        private double lastTickPreciseMs;
        private string appliedAlgorithm;
        private string appliedJitterKey;
        private bool appliedJitterToggle;
        private bool aimLatched;      // aim button held (Hold) or toggled on (Toggle)
        private bool fireDown;         // left mouse button (fire) physically down
        private long aimChangedMs;
        private long fireChangedMs;
        private int aimUpPolls;
        private int fireUpPolls;
        private (int X, int Y)[] wobble = Array.Empty<(int, int)>();
        private string wobbleKey;
        private int wobbleIndex;
        private long wobbleNextMs;
        private string appliedTrigger;
        private bool appliedToggle;
        private int recoilStepIndex;
        private long nextRecoilStepMs;
        private bool oscHighPhase;
        private string lastStatus;
        private bool leftWasRemote;

        /// <summary>
        /// Optional probe: true if the physical mouse input with this name ("Mouse Left", ...) is currently being swallowed by
        /// the input blocker. A swallowed button never updates GetAsyncKeyState, so the stuck-button safety net skips it.
        /// </summary>
        public volatile Func<string, bool> BlockedInputProbe;

        /// <summary>Controls set over the network (see NetworkApiServer); cleared whenever the pad disconnects.</summary>
        public RemoteControlState Remote { get; } = new();

        public bool IsConnected => running;
        public string ControllerName => useXbox ? "Xbox 360" : "PS4";

        public EngineSettings Settings
        {
            get => settings;
            set => settings = value ?? EngineSettings.Default;
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
            lastStatus = null;
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

        /// <summary>Clears low-pass state and the oscillation phase (algorithm switch, gate change, disengage, reconnect).</summary>
        private void ResetFilterState()
        {
            lowPass.Reset();
            oscHighPhase = false;
            lastTickPreciseMs = 0;
        }

        private void ResetState()
        {
            ResetFilterState();
            lastReportSig = ulong.MaxValue;   // force the next tick to send a report
            lastReportMs = -1_000_000;
            submitPending = false;
            disengaged = false;
            turboHeldSinceMs.Clear();
            wheelPulseUntilMs.Clear();
            mouseLook.Reset();
            recoilTriggerDown = false;
            aimLatched = false;
            jitterLatched = false;
            fireDown = false;
            recoilStepIndex = 0;
            oscHighPhase = false;
            leftWasRemote = false;
            Remote.Clear();
        }

        public void Dispose() => Disconnect();

        // ── Input notifications (called from the hook / UI thread) ──────────────

        public void OnKey(string name, bool isDown)
        {
            lock (sync)
            {
                if (isDown) activeKeys.Add(name); else activeKeys.Remove(name);
                EngineSettings s = settings;
                if (isDown && s.DisableKeys.Contains(name)) Engage(s, $"key {name}");
            }
        }

        /// <summary>Mouse button event; <paramref name="button"/> is "Mouse Left", "Mouse Right" or "Mouse Middle".</summary>
        public void OnMouseButton(string button, bool isDown)
        {
            lock (sync)
            {
                EngineSettings s = settings;
                SyncTriggerSettings(s);

                // Mouse buttons can be bound to pad actions like keys ("Mouse Left", "Mouse X1", ...).
                if (isDown) activeKeys.Add(button); else activeKeys.Remove(button);

                // Left click is the fire button.
                if (string.Equals(button, "Mouse Left", StringComparison.OrdinalIgnoreCase))
                {
                    fireDown = isDown;
                    fireChangedMs = NowMs();
                    fireUpPolls = 0;
                }

                // The aim button (right click, middle click) sets the aim state: Hold = while pressed, Toggle = flips per press.
                if (UsesAimGate(s) && string.Equals(s.RecoilTriggerKey, button, StringComparison.OrdinalIgnoreCase))
                {
                    if (s.RecoilToggleMode)
                    {
                        if (isDown) { aimLatched = !aimLatched; aimChangedMs = NowMs(); }
                    }
                    else
                    {
                        aimLatched = isDown;
                        aimChangedMs = NowMs();
                        aimUpPolls = 0;
                    }
                }

                // Right stick filtering has its own button + mode, independent of aim/fire.
                if (UsesJitterGate(s) && string.Equals(s.JitterTriggerKey, button, StringComparison.OrdinalIgnoreCase))
                {
                    if (s.JitterToggleMode)
                    {
                        if (isDown) { jitterLatched = !jitterLatched; jitterChangedMs = NowMs(); }
                    }
                    else
                    {
                        jitterLatched = isDown;
                        jitterChangedMs = NowMs();
                        jitterUpPolls = 0;
                    }
                }

                Reevaluate(s);
            }
        }

        private static bool UsesJitterGate(EngineSettings s)
            => string.Equals(s.JitterTriggerKey, "Mouse Left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.JitterTriggerKey, "Mouse Right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.JitterTriggerKey, "Mouse Middle", StringComparison.OrdinalIgnoreCase);

        /// <summary>RS filtering is on when no button is chosen ("None") or when its button state says so.</summary>
        private bool JitterGateOpen(EngineSettings s) => !UsesJitterGate(s) || jitterLatched;

        /// <summary>
        /// The aim gate only applies when the Aim Trigger is Right or Middle. Off = no aim needed (fires on left click
        /// alone); Left = the aim button is the fire button itself, so there is no separate gate.
        /// </summary>
        private static bool UsesAimGate(EngineSettings s)
            => string.Equals(s.RecoilTriggerKey, "Mouse Right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.RecoilTriggerKey, "Mouse Middle", StringComparison.OrdinalIgnoreCase);

        /// <summary>The pattern runs while the fire button is down AND the aim gate is open. Called under the lock.</summary>
        private void Reevaluate(EngineSettings s)
        {
            bool want = fireDown && (!UsesAimGate(s) || aimLatched);
            if (want && !recoilTriggerDown) ActivateRecoil(s);
            else if (!want && recoilTriggerDown) DeactivateRecoil();
        }

        public void OnWheel(int direction)
        {
            lock (sync)
            {
                EngineSettings s = settings;
                if (s.WheelDisengageEnabled) Engage(s, "mouse wheel");

                // A wheel notch has no "up": it acts as a short press of "Wheel Up" / "Wheel Down" for bindings.
                if (direction != 0)
                {
                    string name = direction > 0 ? MouseInputNames.WheelUp : MouseInputNames.WheelDown;
                    activeKeys.Add(name);
                    wheelPulseUntilMs[name] = NowMs() + WheelPulseMs;
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
            foreach (string name in done) { wheelPulseUntilMs.Remove(name); activeKeys.Remove(name); }
        }

        /// <summary>Changing the trigger button or mode clears any latched/held state (called under the lock).</summary>
        private void SyncTriggerSettings(EngineSettings s)
        {
            if (!string.Equals(appliedAlgorithm, s.JitterAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                appliedAlgorithm = s.JitterAlgorithm;
                ResetFilterState();
            }

            if (!string.Equals(appliedJitterKey, s.JitterTriggerKey, StringComparison.OrdinalIgnoreCase) || appliedJitterToggle != s.JitterToggleMode)
            {
                appliedJitterKey = s.JitterTriggerKey;
                appliedJitterToggle = s.JitterToggleMode;
                jitterLatched = false;
            }

            if (string.Equals(appliedTrigger, s.RecoilTriggerKey, StringComparison.OrdinalIgnoreCase) && appliedToggle == s.RecoilToggleMode)
                return;
            appliedTrigger = s.RecoilTriggerKey;
            appliedToggle = s.RecoilToggleMode;
            aimLatched = false;
            DeactivateRecoil();
            Reevaluate(s);
        }

        private void ActivateRecoil(EngineSettings s)
        {
            long now = NowMs();
            recoilTriggerDown = true;
            recoilStepIndex = 0;
            wobbleIndex = 0;
            wobbleNextMs = 0;
            recoilStartAtMs = now + Math.Max(0, s.RecoilStartDelayMs);
            if (s.RecoilSteps.Length > 0)
                nextRecoilStepMs = recoilStartAtMs + (long)Math.Round(s.RecoilSteps[0].IntervalMs);
        }

        private void DeactivateRecoil()
        {
            recoilTriggerDown = false;
            recoilStepIndex = 0;
        }

        /// <summary>
        /// Safety net: if the hook ever misses a button-up (e.g. Windows dropped the hook), release the fire button
        /// (and the aim button in Hold mode) once the physical button has been seen up for several consecutive ticks.
        /// </summary>
        private bool PhysicalPollable(string button)
        {
            Func<string, bool> probe = BlockedInputProbe;
            return probe == null || !probe(button);
        }

        private void PollStuckButtons(EngineSettings s, long now)
        {
            if (fireDown && PhysicalPollable("Mouse Left") && MouseState.TryIsDown("Mouse Left", out bool leftDown))
            {
                if (leftDown) fireUpPolls = 0;
                else if (now - fireChangedMs >= 30 && ++fireUpPolls >= 3) fireDown = false;
            }

            if (jitterLatched && !s.JitterToggleMode && UsesJitterGate(s) && PhysicalPollable(s.JitterTriggerKey) && MouseState.TryIsDown(s.JitterTriggerKey, out bool jitDown))
            {
                if (jitDown) jitterUpPolls = 0;
                else if (now - jitterChangedMs >= 30 && ++jitterUpPolls >= 3) jitterLatched = false;
            }

            if (aimLatched && !s.RecoilToggleMode && UsesAimGate(s) && PhysicalPollable(s.RecoilTriggerKey) && MouseState.TryIsDown(s.RecoilTriggerKey, out bool aimDown))
            {
                if (aimDown) aimUpPolls = 0;
                else if (now - aimChangedMs >= 30 && ++aimUpPolls >= 3) aimLatched = false;
            }
        }

        private void Engage(EngineSettings s, string source)
        {
            if (s.DisengageMs <= 0) { disengaged = false; return; }
            disengaged = true;
            disengagedUntilMs = NowMs() + s.DisengageMs;
            RaiseStatus($"Status: Temporarily disabled by {source}");
        }

        private long NowMs() => clock.ElapsedMilliseconds;

        // ── Output thread ───────────────────────────────────────────────────────

        /// <summary>
        /// Output period while filtering: the chosen rate, but for the low-pass wiggle never slower than 1/8 of its wave
        /// (so a small N, whose wave is only 16 ms x N long, is still sampled properly).
        /// </summary>
        private static double FastPeriodMs(EngineSettings s)
        {
            double period = Math.Clamp(s.FilterPeriodMs, 1, 50);
            if (string.Equals(s.JitterAlgorithm, "LowPass", StringComparison.OrdinalIgnoreCase))
            {
                double wave = StickMath.SoftWigglePeriodMs(s.JitterValue);
                if (wave > 0) period = Math.Min(period, Math.Max(1.0, wave / 8.0));
            }
            return period;
        }

        private void Loop()
        {
            bool highRes = false;
            if (OperatingSystem.IsWindows()) { try { highRes = NativeTime.timeBeginPeriod(1) == 0; } catch { } }

            try
            {
                double next = clock.Elapsed.TotalMilliseconds;
                while (running)
                {
                    bool jitterActive;
                    try
                    {
                        jitterActive = Tick();
                        if (submitPending)
                        {
                            submitPending = false;
                            // The driver call happens outside the lock so mouse/keyboard hooks never wait on it.
                            IXboxPad x = xbox; IDs4Pad d = ds4;
                            if (useXbox) x?.SubmitReport(); else d?.SubmitReport();
                        }
                    }
                    catch (Exception ex)
                    {
                        running = false;
                        try { Faulted?.Invoke(ex); } catch { }
                        break;
                    }

                    next += jitterActive ? FastPeriodMs(settings) : NormalPeriodMs;
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

        /// <summary>Builds and submits one report. Returns true while jitter mode is active.</summary>
        private bool Tick()
        {
            EngineSettings s = settings;
            lock (sync)
            {
                long now = NowMs();
                if (disengaged && now >= disengagedUntilMs) disengaged = false;
                ExpireWheelPulses(now);

                SyncTriggerSettings(s);
                if (aimLatched && s.RecoilToggleMode && !s.RecoilEnabled) aimLatched = false; // toggled-on aim doesn't survive disabling recoil
                PollStuckButtons(s, now);
                Reevaluate(s);

                if (disengaged)
                {
                    ResetFilterState();
                    SubmitNeutral();
                    RaiseStatus("Status: Temporarily disabled");
                    return false;
                }

                // Base drift: identical to the original app.
                // Base drift: Down / Up / Left / Right each pull straight along one axis from the center.
                (int percentX, int percentY) = GetBasePull(s, s.DriftValue);
                // Right stick filtering (separate from drift/recoil): the gate comes from Filter Button + Hold/Toggle.
                bool filterGate = JitterGateOpen(s);
                bool lowPassMode = string.Equals(s.JitterAlgorithm, "LowPass", StringComparison.OrdinalIgnoreCase);
                int jitterAmp = filterGate && !lowPassMode ? StickMath.JitterAmplitude(s.JitterValue) : 0; // oscillation (Y only)
                double tauMs = filterGate && lowPassMode ? StickMath.SmoothingTauMs(s.JitterValue) : 0.0;  // low-pass (X and Y)

                bool recoilActive = false;
                if (s.RecoilEnabled && recoilTriggerDown && s.RecoilSteps.Length > 0 && now >= recoilStartAtMs)
                {
                    int guard = 0;
                    while (now >= nextRecoilStepMs && guard++ < s.RecoilSteps.Length * 2)
                    {
                        if (recoilStepIndex >= s.RecoilSteps.Length - 1)
                        {
                            if (!s.RecoilLoop) break;   // hold the last step (original behavior)
                            recoilStepIndex = 0;        // Loop: start again from step 1
                        }
                        else recoilStepIndex++;
                        long stepMs = (long)Math.Round(s.RecoilSteps[recoilStepIndex].IntervalMs);
                        nextRecoilStepMs = now + (s.RecoilLoop ? Math.Max(1L, stepMs) : stepMs);
                    }
                    RecoilStep step = s.RecoilSteps[Math.Min(recoilStepIndex, s.RecoilSteps.Length - 1)];
                    percentX = StickMath.ClampPercent(step.X);
                    percentY = StickMath.ClampPercent(step.Y);
                    ApplyCenterRadius(s, now, ref percentX, ref percentY);
                    recoilActive = true;
                }

                if (jitterAmp > 0) oscHighPhase = !oscHighPhase; else oscHighPhase = false;

                // Exponential low-pass on the final target (X and Y, in percent); state is cleared whenever it is off.
                // The filtering wiggle always shakes the axis PERPENDICULAR to the drift: Left/Right drift on X so the wiggle is on Y;
                // Up/Down drift on Y so the wiggle is on X.
                bool wiggleOnX = PullDirections.IsVertical(s.PullDirection);
                double outX = percentX, outY = percentY;
                double nowPrecise = clock.Elapsed.TotalMilliseconds;
                if (tauMs > 0)
                {
                    double dtMs = lastTickPreciseMs > 0 ? Math.Clamp(nowPrecise - lastTickPreciseMs, 0.0, 100.0) : 0.0;
                    // Soft wiggle: a symmetric square wave on the wiggle axis that the same filter rounds (not applied on the
                    // seeding tick, so the filter starts exactly on the target). Changes on X and Y are smoothed by the same filter.
                    double wiggle = 0.0;
                    if (lowPass.Seeded)
                        wiggle = StickMath.SoftWiggleDrivePercent(s.JitterValue, wiggleOnX ? percentX : percentY)
                               * StickMath.SquareWave(nowPrecise, StickMath.SoftWigglePeriodMs(s.JitterValue));
                    (outX, outY) = lowPass.Step(percentX + (wiggleOnX ? wiggle : 0.0), percentY + (wiggleOnX ? 0.0 : wiggle),
                        StickMath.LowPassAlpha(tauMs, dtMs));
                }
                else lowPass.Reset();
                lastTickPreciseMs = nowPrecise;

                // Mouse look: relative mouse movement drives the right stick on top of drift/recoil/filtering.
                if (s.MouseLookEnabled)
                {
                    (double mx, double my) = mouseLook.Step(nowPrecise, s.MouseSensitivityX, s.MouseSensitivityY,
                        s.MouseCurve, s.MouseGameDeadZonePercent / 100.0, s.MouseReleaseMs, s.MouseInvertY);
                    if (useXbox) my = -my;   // mouse down is +Y here (PS4 convention); on Xbox +Y is up
                    outX = Math.Clamp(outX + mx, -100.0, 100.0);
                    outY = Math.Clamp(outY + my, -100.0, 100.0);
                }
                else mouseLook.Reset();

                RemoteFrame rf = Remote.Snapshot(now);
                int mask = PressedMask(s, now) | rf.ButtonMask;
                if (useXbox && xbox != null)
                {
                    short rx = StickMath.PercentToXboxAxis(outX);
                    short rsY = StickMath.PercentToXboxAxis(outY);
                    if (jitterAmp > 0)
                    {
                        if (wiggleOnX) rx = StickMath.XboxOscillateY(rx, jitterAmp, oscHighPhase);   // helper is axis-agnostic
                        else rsY = StickMath.XboxOscillateY(rsY, jitterAmp, oscHighPhase);
                    }
                    if (rf.RightActive) { rx = RemoteControlState.ToXboxAxis(rf.Rx); rsY = RemoteControlState.ToXboxAxisY(rf.Ry); }
                    xbox.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                    xbox.SetAxisValue(Xbox360Axis.RightThumbY, rsY);
                    ApplyXboxBindings(mask);
                    ApplyXboxRemote(rf);
                    MarkReport(rx, rsY, mask, now, rf.Signature);
                }
                else if (!useXbox && ds4 != null)
                {
                    byte rx = StickMath.PercentToDs4Axis(outX);
                    byte rsY = StickMath.PercentToDs4Axis(outY);
                    if (jitterAmp > 0)
                    {
                        if (wiggleOnX) rx = StickMath.Ds4OscillateY(rx, jitterAmp, oscHighPhase);    // helper is axis-agnostic
                        else rsY = StickMath.Ds4OscillateY(rsY, jitterAmp, oscHighPhase);
                    }
                    if (rf.RightActive) { rx = RemoteControlState.ToDs4Axis(rf.Rx); rsY = RemoteControlState.ToDs4Axis(rf.Ry); }
                    ds4.SetAxisValue(DualShock4Axis.RightThumbX, rx);
                    ds4.SetAxisValue(DualShock4Axis.RightThumbY, rsY);
                    ApplyDs4Bindings(mask);
                    ApplyDs4Remote(rf);
                    MarkReport(rx, rsY, mask, now, rf.Signature);
                }

                string ctrl = ControllerName;
                string key = UsesAimGate(s)
                    ? $"{s.RecoilTriggerKey} ({(s.RecoilToggleMode ? "toggle" : "hold")}) + Mouse Left"
                    : "Mouse Left";
                if (recoilActive)
                    RaiseStatus($"Status: {ctrl} - recoil step {recoilStepIndex + 1}/{s.RecoilSteps.Length} on {key}");
                else if (jitterAmp > 0)
                    RaiseStatus($"Status: {ctrl} connected - RS oscillation +/-{jitterAmp} active");
                else if (tauMs > 0)
                    RaiseStatus($"Status: {ctrl} connected - RS low-pass {tauMs:0} ms, soft wiggle +/-{StickMath.JitterAmplitude(s.JitterValue)} active");
                else
                    RaiseStatus($"Status: {ctrl} connected - base drift active");

                return jitterAmp > 0 || tauMs > 0;   // oscillation and the low-pass wiggle both need the fast loop
            }
        }

        /// <summary>
        /// Center Radius: "Lift" (new) pushes small non-zero steps out to the radius;
        /// "Shake" / "Circle" (old) add a centered wobble of that radius around the step.
        /// </summary>
        private void ApplyCenterRadius(EngineSettings s, long now, ref int percentX, ref int percentY)
        {
            string mode = s.RecoilCenterRadiusMode;
            if (string.Equals(mode, "Shake", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "Circle", StringComparison.OrdinalIgnoreCase))
            {
                string key = mode + ":" + s.RecoilCenterRadius;
                if (key != wobbleKey) { wobbleKey = key; wobble = WobblePattern.Build(mode, s.RecoilCenterRadius); wobbleIndex = 0; }
                if (wobble.Length == 0) return;

                if (wobbleNextMs == 0) wobbleNextMs = now + WobblePattern.StepIntervalMs;
                while (now >= wobbleNextMs)
                {
                    wobbleIndex++;
                    wobbleNextMs += WobblePattern.StepIntervalMs;
                    if (now - wobbleNextMs > 100) wobbleNextMs = now + WobblePattern.StepIntervalMs; // after a stall
                }
                var w = wobble[wobbleIndex % wobble.Length];
                percentX = StickMath.ClampPercent(percentX + w.X);
                percentY = StickMath.ClampPercent(percentY + w.Y);
            }
            else
            {
                (percentX, percentY) = StickMath.ApplyCenterRadius(percentX, percentY, s.RecoilCenterRadius);
            }
        }

        /// <summary>
        /// Base drift vector: one straight axis, the plain sign of the drift percent on every controller type
        /// (negative = left / up, positive = right / down), exactly the same form for all four directions:
        ///   Left (-d, 0)   Right (d, 0)   Up (0, -d)   Down (0, d)
        /// Note: Y is the controller's native Y, so "Down" points down on PS4 (positive Y = down) but up on Xbox 360 (positive Y = up).
        /// </summary>
        private static (int X, int Y) GetBasePull(EngineSettings s, int d) => PullDirections.Normalize(s.PullDirection) switch
        {
            "Up" => (0, -d),
            "Left" => (-d, 0),
            "Right" => (d, 0),
            _ => (0, d)   // Down (and the old "Center")
        };

        /// <summary>Sets a neutral stick (buttons still applied) and marks a report; the driver call happens in Loop.</summary>
        private void SubmitNeutral()
        {
            EngineSettings s = settings;
            long now = NowMs();
            int mask = PressedMask(s, now);
            if (useXbox && xbox != null)
            {
                xbox.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                xbox.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                ApplyXboxBindings(mask);
                MarkReport(0, 0, mask, now);
            }
            else if (!useXbox && ds4 != null)
            {
                ds4.SetAxisValue(DualShock4Axis.RightThumbX, StickMath.Ds4Center);
                ds4.SetAxisValue(DualShock4Axis.RightThumbY, StickMath.Ds4Center);
                ApplyDs4Bindings(mask);
                MarkReport(StickMath.Ds4Center, StickMath.Ds4Center, mask, now);
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
        }

        /// <summary>Remote left stick and analog triggers (the right stick and buttons are merged in Tick). Under the lock.</summary>
        private void ApplyXboxRemote(RemoteFrame rf)
        {
            if (rf.LeftActive || leftWasRemote)
            {
                xbox.SetAxisValue(Xbox360Axis.LeftThumbX, rf.LeftActive ? RemoteControlState.ToXboxAxis(rf.Lx) : (short)0);
                xbox.SetAxisValue(Xbox360Axis.LeftThumbY, rf.LeftActive ? RemoteControlState.ToXboxAxisY(rf.Ly) : (short)0);
                leftWasRemote = rf.LeftActive;
            }
            if (rf.Lt >= 0) xbox.SetSliderValue(Xbox360Slider.LeftTrigger, RemoteControlState.ToTriggerByte(rf.Lt));
            if (rf.Rt >= 0) xbox.SetSliderValue(Xbox360Slider.RightTrigger, RemoteControlState.ToTriggerByte(rf.Rt));
        }

        private void ApplyDs4Remote(RemoteFrame rf)
        {
            if (rf.LeftActive || leftWasRemote)
            {
                ds4.SetAxisValue(DualShock4Axis.LeftThumbX, rf.LeftActive ? RemoteControlState.ToDs4Axis(rf.Lx) : StickMath.Ds4Center);
                ds4.SetAxisValue(DualShock4Axis.LeftThumbY, rf.LeftActive ? RemoteControlState.ToDs4Axis(rf.Ly) : StickMath.Ds4Center);
                leftWasRemote = rf.LeftActive;
            }
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

        private static class MouseState
        {
            /// <summary>Physical state of the trigger button (accounts for swapped primary/secondary buttons).</summary>
            internal static bool TryIsDown(string trigger, out bool down)
            {
                down = false;
                if (!OperatingSystem.IsWindows()) return false;
                bool swapped = MouseButtonState.IsSwapped();
                int vk;
                switch (trigger)
                {
                    case "Mouse Left": vk = MouseButtonState.ResolveVk(MouseButtonState.VkLeft, swapped); break;
                    case "Mouse Right": vk = MouseButtonState.ResolveVk(MouseButtonState.VkRight, swapped); break;
                    case "Mouse Middle": vk = MouseButtonState.VkMiddle; break;
                    default: return false;
                }
                down = MouseButtonState.IsDown(vk);
                return true;
            }
        }

        private static class NativeTime
        {
            [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint ms);
            [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint ms);
        }
    }
}
