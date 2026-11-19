using System;
using System.Diagnostics;
using System.Threading;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadEnhancer
{
    /// <summary>
    /// The VHF backend as the engine sees it: the same Xbox 360 / DualShock 4 pad calls the ViGEm and network pads take, turned into
    /// the driver's normalized input (buttons as bits, sticks signed with positive = up and right, triggers 0..255). The engine keeps
    /// all processing; this class only formats the final state, keeps the virtual controller alive and reports what the driver says.
    /// Everything runs on the engine's output thread (Connect and Disconnect on the UI thread, before and after it); a lock keeps the
    /// two from touching the client at once.
    /// </summary>
    internal sealed class VhfPad : IXboxPad, IDs4Pad
    {
        private const long HeartbeatMs = 100;        // resend an unchanged state this often, so the driver's view never goes stale
        private const long RecoverIntervalMs = 500;  // between attempts to bring a lost controller back
        private const int FeedbackEveryTicks = 4;    // poll the driver's feedback slot every N reports
        private const int CreateAttempts = 6;        // ERROR_BUSY: the previous child is still being removed
        private const int CreateRetryMs = 100;

        private readonly VhfClient client;
        private readonly VhfProfileId profile;
        private readonly uint slot;
        private readonly Func<long> nowMs;
        private readonly Action<int> sleep;
        private readonly object gate = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private uint buttons;
        private short leftX, leftY, rightX, rightY;
        private byte leftTrigger, rightTrigger;

        private VhfInput last;
        private bool haveLast, created, lost;
        private long lastSendMs, lastRecoverMs = long.MinValue;
        private int tick;
        private string lastStatus;

        public VhfPad(VhfClient client, VhfProfileId profile, uint slot = 0, Func<long> nowMs = null, Action<int> sleep = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.profile = profile;
            this.slot = slot;
            this.nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
            this.sleep = sleep ?? (ms => Thread.Sleep(ms));
        }

        public VhfProfileId Profile => profile;
        /// <summary>The last Win32 error a submit returned (0 when the last one worked).</summary>
        public int LastError { get; private set; }
        public VhfFeedback LastFeedback { get; private set; }
        public bool ControllerLost => lost;

        /// <summary>A line for the status bar: a lost controller, a restored one, or an error. Raised only when the text changes.</summary>
        public event Action<string> StatusChanged;
        /// <summary>Rumble / lightbar / LED output a game wrote to the virtual pad. Display only: nothing is forwarded to hardware.</summary>
        public event Action<VhfFeedback> FeedbackReceived;

        // ── Connection ──────────────────────────────────────────────────────────

        public void Connect()
        {
            lock (gate)
            {
                int error = client.Connect();
                if (error != VhfWin32.ErrorSuccess) throw new VhfException(error, VhfErrors.Describe(error));

                error = CreateWithRetry();
                if (error != VhfWin32.ErrorSuccess)
                {
                    client.Close();
                    string message = error == VhfWin32.ErrorNotSupported
                        ? $"The installed driver does not provide the {profile} profile."
                        : VhfErrors.Describe(error);
                    throw new VhfException(error, message);
                }
                created = true;
                lost = false;
                haveLast = false;
                tick = 0;
                lastStatus = null;
            }
        }

        /// <summary>Removes the virtual controller and closes the handle. Best effort: closing the handle releases the slot anyway.</summary>
        public void Disconnect()
        {
            lock (gate)
            {
                if (created && client.Connected) client.DestroyController(slot);
                created = false;
                lost = false;
                client.Close();
            }
        }

        private int CreateWithRetry()
        {
            int error = VhfWin32.ErrorSuccess;
            for (int attempt = 0; attempt < CreateAttempts; attempt++)
            {
                error = client.CreateController(slot, profile);
                if (error != VhfWin32.ErrorBusy) return error;
                sleep(CreateRetryMs);
            }
            return error;
        }

        // ── Xbox 360 calls ──────────────────────────────────────────────────────

        public void SetAxisValue(Xbox360Axis axis, short value)
        {
            lock (gate)
            {
                // Xbox sticks are already positive = up and right, which is the driver's convention.
                if (axis == Xbox360Axis.LeftThumbX) leftX = value;
                else if (axis == Xbox360Axis.LeftThumbY) leftY = value;
                else if (axis == Xbox360Axis.RightThumbX) rightX = value;
                else if (axis == Xbox360Axis.RightThumbY) rightY = value;
            }
        }

        public void SetSliderValue(Xbox360Slider slider, byte value)
        {
            lock (gate)
            {
                if (slider == Xbox360Slider.LeftTrigger) leftTrigger = value; else rightTrigger = value;
            }
        }

        public void SetButtonState(Xbox360Button button, bool pressed)
        {
            uint bit = button switch
            {
                var x when x == Xbox360Button.A => VhfButtons.South,
                var x when x == Xbox360Button.B => VhfButtons.East,
                var x when x == Xbox360Button.X => VhfButtons.West,
                var x when x == Xbox360Button.Y => VhfButtons.North,
                var x when x == Xbox360Button.LeftShoulder => VhfButtons.LeftShoulder,
                var x when x == Xbox360Button.RightShoulder => VhfButtons.RightShoulder,
                var x when x == Xbox360Button.Back => VhfButtons.Back,
                var x when x == Xbox360Button.Start => VhfButtons.Start,
                var x when x == Xbox360Button.LeftThumb => VhfButtons.LeftStick,
                var x when x == Xbox360Button.RightThumb => VhfButtons.RightStick,
                var x when x == Xbox360Button.Guide => VhfButtons.Home,
                var x when x == Xbox360Button.Up => VhfButtons.DpadUp,
                var x when x == Xbox360Button.Down => VhfButtons.DpadDown,
                var x when x == Xbox360Button.Left => VhfButtons.DpadLeft,
                var x when x == Xbox360Button.Right => VhfButtons.DpadRight,
                _ => 0u
            };
            SetBit(bit, pressed);
        }

        // ── DualShock 4 / DualSense calls ───────────────────────────────────────

        public void SetAxisValue(DualShock4Axis axis, byte value)
        {
            // A DualShock 4 axis is 0..255 with 128 in the middle and Y counting down; the driver wants signed, positive = up/right.
            short x = ClampToShort((value - 128) * 257);
            short y = ClampToShort((128 - value) * 257);
            lock (gate)
            {
                if (axis == DualShock4Axis.LeftThumbX) leftX = x;
                else if (axis == DualShock4Axis.LeftThumbY) leftY = y;
                else if (axis == DualShock4Axis.RightThumbX) rightX = x;
                else if (axis == DualShock4Axis.RightThumbY) rightY = y;
            }
        }

        public void SetSliderValue(DualShock4Slider slider, byte value)
        {
            lock (gate)
            {
                if (slider == DualShock4Slider.LeftTrigger) leftTrigger = value; else rightTrigger = value;
            }
        }

        public void SetButtonState(DualShock4Button button, bool pressed)
        {
            uint bit = button switch
            {
                var x when x == DualShock4Button.Cross => VhfButtons.South,
                var x when x == DualShock4Button.Circle => VhfButtons.East,
                var x when x == DualShock4Button.Square => VhfButtons.West,
                var x when x == DualShock4Button.Triangle => VhfButtons.North,
                var x when x == DualShock4Button.ShoulderLeft => VhfButtons.LeftShoulder,
                var x when x == DualShock4Button.ShoulderRight => VhfButtons.RightShoulder,
                var x when x == DualShock4Button.Share => VhfButtons.Back,
                var x when x == DualShock4Button.Options => VhfButtons.Start,
                var x when x == DualShock4Button.ThumbLeft => VhfButtons.LeftStick,
                var x when x == DualShock4Button.ThumbRight => VhfButtons.RightStick,
                _ => 0u   // the L2/R2 "buttons": the analog trigger value already carries them
            };
            SetBit(bit, pressed);
        }

        public void SetButtonState(DualShock4SpecialButton button, bool pressed)
        {
            uint bit = button == DualShock4SpecialButton.Ps ? VhfButtons.Home
                : button == DualShock4SpecialButton.Touchpad ? VhfButtons.Touchpad : 0u;
            SetBit(bit, pressed);
        }

        public void SetDPadDirection(DualShock4DPadDirection direction)
        {
            uint bits = 0;
            if (direction == DualShock4DPadDirection.North || direction == DualShock4DPadDirection.Northeast || direction == DualShock4DPadDirection.Northwest) bits |= VhfButtons.DpadUp;
            if (direction == DualShock4DPadDirection.South || direction == DualShock4DPadDirection.Southeast || direction == DualShock4DPadDirection.Southwest) bits |= VhfButtons.DpadDown;
            if (direction == DualShock4DPadDirection.West || direction == DualShock4DPadDirection.Northwest || direction == DualShock4DPadDirection.Southwest) bits |= VhfButtons.DpadLeft;
            if (direction == DualShock4DPadDirection.East || direction == DualShock4DPadDirection.Northeast || direction == DualShock4DPadDirection.Southeast) bits |= VhfButtons.DpadRight;
            lock (gate) buttons = (buttons & ~VhfButtons.DpadMask) | bits;
        }

        private void SetBit(uint bit, bool pressed)
        {
            if (bit == 0) return;
            lock (gate) buttons = pressed ? buttons | bit : buttons & ~bit;
        }

        internal static short ClampToShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

        // ── Output ──────────────────────────────────────────────────────────────

        /// <summary>The state the next report will carry (for tests and the status panel).</summary>
        internal VhfInput CurrentState { get { lock (gate) return new VhfInput(slot, buttons, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger); } }

        public void SubmitReport()
        {
            string status = null;
            VhfFeedback feedback = null;
            lock (gate)
            {
                if (!created) return;
                long now = nowMs();
                if (lost && !TryRecover(now, out status)) { Publish(status, null); return; }

                var input = new VhfInput(slot, buttons, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);
                if (!haveLast || !input.Equals(last) || now - lastSendMs >= HeartbeatMs)
                {
                    int error = client.SubmitInput(input);
                    if (error == VhfWin32.ErrorSuccess) { last = input; haveLast = true; lastSendMs = now; LastError = 0; }
                    else status = OnSubmitError(error, now);
                }

                if (!lost && ++tick % FeedbackEveryTicks == 0) feedback = PollFeedbackNow(ref status);
            }
            Publish(status, feedback);
        }

        private string OnSubmitError(int error, long now)
        {
            LastError = error;
            if (error == VhfWin32.ErrorDeviceRemoved || error == VhfWin32.ErrorNotFound || error == VhfWin32.ErrorNotReady || error == VhfWin32.ErrorInvalidHandle)
            {
                lost = true;
                lastRecoverMs = now - RecoverIntervalMs;   // first recovery attempt on the next report
                return "Virtual controller lost (" + VhfErrors.Describe(error) + ") Trying to bring it back.";
            }
            return "Virtual controller: " + VhfErrors.Describe(error);
        }

        /// <summary>Recreates the controller after sleep/resume or a driver restart, at most every <see cref="RecoverIntervalMs"/>.</summary>
        private bool TryRecover(long now, out string status)
        {
            status = null;
            if (now - lastRecoverMs < RecoverIntervalMs) return false;
            lastRecoverMs = now;

            int error = client.Connected ? VhfWin32.ErrorSuccess : client.Connect();
            if (error == VhfWin32.ErrorSuccess)
            {
                client.DestroyController(slot);   // a stale child may still exist; creating over it returns ERROR_BUSY
                error = client.CreateController(slot, profile);
            }
            if (error == VhfWin32.ErrorSuccess)
            {
                lost = false;
                haveLast = false;
                status = "Virtual controller restored.";
                return true;
            }
            // keep the handle only while the driver says it is merely busy; otherwise reconnect on the next attempt
            if (error != VhfWin32.ErrorBusy) client.Close();
            status = "Virtual controller is not back yet: " + VhfErrors.Describe(error);
            return false;
        }

        private VhfFeedback PollFeedbackNow(ref string status)
        {
            VhfFeedback newest = null;
            for (int i = 0; i < 4; i++)
            {
                int error = client.PollFeedback(slot, out VhfFeedback fb);
                if (error == VhfWin32.ErrorSuccess) { newest = fb; continue; }
                if (error == VhfWin32.ErrorNoMoreItems) break;
                if (error == VhfWin32.ErrorDeviceRemoved) { lost = true; lastRecoverMs = nowMs() - RecoverIntervalMs; status = "Virtual controller lost (" + VhfErrors.Describe(error) + ") Trying to bring it back."; }
                else status = "Virtual controller feedback: " + VhfErrors.Describe(error);
                break;
            }
            if (newest != null) LastFeedback = newest;
            return newest;
        }

        private void Publish(string status, VhfFeedback feedback)
        {
            if (status != null && status != lastStatus) { lastStatus = status; StatusChanged?.Invoke(status); }
            if (feedback != null) FeedbackReceived?.Invoke(feedback);
        }
    }
}
