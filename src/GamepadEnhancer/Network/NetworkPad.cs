using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadEnhancer
{
    /// <summary>
    /// A "pad" that, instead of talking to ViGEmBus, sends its state to another PC as km.controller(id,value) commands.
    /// Only values that changed are sent; a quiet pad sends a cheap ping so the other side's idle timeout never fires.
    /// Values follow the receiver's convention: sticks -32768..32767 with +Y down, triggers 0..1023, buttons 0/1.
    /// </summary>
    internal sealed class NetworkPad : IXboxPad, IDs4Pad
    {
        private const long PingMs = 5000;
        private readonly RemoteLink link;
        private readonly bool xbox;
        private readonly int[] value = new int[RemoteControlState.IdCount];
        private readonly int[] sent = new int[RemoteControlState.IdCount];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastSendMs;
        private volatile bool resendAll = true;
        private volatile bool resendMouse = true;

        public NetworkPad(RemoteTarget target, bool xbox)
        {
            this.xbox = xbox;
            link = new RemoteLink(target);
            link.Reconnected += () => { resendAll = true; resendMouse = true; };
            Array.Fill(sent, int.MinValue);
            // neutral starting state: sticks centred, triggers and buttons released
        }

        public RemoteLink Link => link;

        public void Connect() => link.Open();

        public void Disconnect()
        {
            StopMouse();
            link.Dispose();
        }

        // ── Mouse buttons (km.mouse) over the same link ──
        private const int MousePollMs = 4;
        private const long MouseKeepAliveMs = 1000;
        private Thread mouseThread;
        private volatile bool mouseRunning;

        /// <summary>
        /// Sends this PC's physical mouse buttons to the other side as km.mouse(mask): on every change, plus a keep-alive
        /// every second, and again after a reconnect. Runs beside the pad on the same connection.
        /// </summary>
        public void StartMouse(Func<byte> readMask)
        {
            StopMouse();
            mouseRunning = true;
            mouseThread = new Thread(() => MouseLoop(readMask)) { IsBackground = true, Name = "Mouse buttons sender" };
            mouseThread.Start();
        }

        private void StopMouse()
        {
            mouseRunning = false;
            Thread t = mouseThread;
            mouseThread = null;
            if (t != null && t != Thread.CurrentThread) t.Join(500);
        }

        private void MouseLoop(Func<byte> readMask)
        {
            int last = -1;
            long lastSend = 0;
            var clock = Stopwatch.StartNew();
            while (mouseRunning)
            {
                try
                {
                    int mask = readMask();
                    long now = clock.ElapsedMilliseconds;
                    if (resendMouse || mask != last || now - lastSend >= MouseKeepAliveMs)
                    {
                        if (link.TrySend("km.mouse(" + mask + ")\n"))
                        {
                            last = mask;
                            lastSend = now;
                            resendMouse = false;
                        }
                    }
                }
                catch { /* keep going; the link reports its own problems */ }
                Thread.Sleep(MousePollMs);
            }
        }

        // ── Xbox 360 ──
        public void SetAxisValue(Xbox360Axis axis, short v)
        {
            if (axis == Xbox360Axis.LeftThumbX) value[RemoteControlState.IdLeftX] = v;
            else if (axis == Xbox360Axis.LeftThumbY) value[RemoteControlState.IdLeftY] = Math.Clamp(-(int)v, short.MinValue, short.MaxValue);   // Xbox +Y is up
            else if (axis == Xbox360Axis.RightThumbX) value[RemoteControlState.IdRightX] = v;
            else if (axis == Xbox360Axis.RightThumbY) value[RemoteControlState.IdRightY] = Math.Clamp(-(int)v, short.MinValue, short.MaxValue);
        }

        public void SetSliderValue(Xbox360Slider slider, byte v) =>
            value[slider == Xbox360Slider.LeftTrigger ? RemoteControlState.IdLeftTrigger : RemoteControlState.IdRightTrigger] = TriggerOut(v);

        public void SetButtonState(Xbox360Button b, bool pressed)
        {
            int id = b switch
            {
                var x when x == Xbox360Button.A => 0, var x when x == Xbox360Button.B => 1,
                var x when x == Xbox360Button.X => 2, var x when x == Xbox360Button.Y => 3,
                var x when x == Xbox360Button.LeftShoulder => 4, var x when x == Xbox360Button.RightShoulder => 5,
                var x when x == Xbox360Button.Back => 6, var x when x == Xbox360Button.Start => 7,
                var x when x == Xbox360Button.LeftThumb => 8, var x when x == Xbox360Button.RightThumb => 9,
                var x when x == Xbox360Button.Up => 16, var x when x == Xbox360Button.Down => 17,
                var x when x == Xbox360Button.Left => 18, var x when x == Xbox360Button.Right => 19,
                _ => -1
            };
            if (id >= 0) value[id] = pressed ? 1 : 0;
        }

        // ── DualShock 4 ──
        public void SetAxisValue(DualShock4Axis axis, byte v)
        {
            int n = (v << 8) - 32768;   // 128 -> 0, +Y is already down
            if (axis == DualShock4Axis.LeftThumbX) value[RemoteControlState.IdLeftX] = n;
            else if (axis == DualShock4Axis.LeftThumbY) value[RemoteControlState.IdLeftY] = n;
            else if (axis == DualShock4Axis.RightThumbX) value[RemoteControlState.IdRightX] = n;
            else if (axis == DualShock4Axis.RightThumbY) value[RemoteControlState.IdRightY] = n;
        }

        public void SetSliderValue(DualShock4Slider slider, byte v) =>
            value[slider == DualShock4Slider.LeftTrigger ? RemoteControlState.IdLeftTrigger : RemoteControlState.IdRightTrigger] = TriggerOut(v);

        public void SetButtonState(DualShock4Button b, bool pressed)
        {
            int id = b switch
            {
                var x when x == DualShock4Button.Cross => 0, var x when x == DualShock4Button.Circle => 1,
                var x when x == DualShock4Button.Square => 2, var x when x == DualShock4Button.Triangle => 3,
                var x when x == DualShock4Button.ShoulderLeft => 4, var x when x == DualShock4Button.ShoulderRight => 5,
                var x when x == DualShock4Button.Share => 6, var x when x == DualShock4Button.Options => 7,
                var x when x == DualShock4Button.ThumbLeft => 8, var x when x == DualShock4Button.ThumbRight => 9,
                _ => -1   // TriggerLeft/Right buttons: the analog trigger value already carries them
            };
            if (id >= 0) value[id] = pressed ? 1 : 0;
        }

        /// <summary>The network protocol has no id for the PS button, so it is not sent to the other PC.</summary>
        public void SetButtonState(DualShock4SpecialButton b, bool pressed) { }

        public void SetDPadDirection(DualShock4DPadDirection d)
        {
            bool up = d == DualShock4DPadDirection.North || d == DualShock4DPadDirection.Northeast || d == DualShock4DPadDirection.Northwest;
            bool down = d == DualShock4DPadDirection.South || d == DualShock4DPadDirection.Southeast || d == DualShock4DPadDirection.Southwest;
            bool left = d == DualShock4DPadDirection.West || d == DualShock4DPadDirection.Northwest || d == DualShock4DPadDirection.Southwest;
            bool right = d == DualShock4DPadDirection.East || d == DualShock4DPadDirection.Northeast || d == DualShock4DPadDirection.Southeast;
            value[16] = up ? 1 : 0; value[17] = down ? 1 : 0; value[18] = left ? 1 : 0; value[19] = right ? 1 : 0;
        }

        /// <summary>Same ids and ranges the receiver expects; 0..255 trigger byte to 0..1023.</summary>
        private static int TriggerOut(byte v) => (v * RemoteControlState.MaxTrigger + 127) / 255;

        public void SubmitReport()
        {
            bool all = resendAll;
            var sb = new StringBuilder();
            for (int id = 0; id < value.Length; id++)
            {
                if (!all && sent[id] == value[id]) continue;
                // A released button is sent as 0 (the receiver treats it as not driven), a trigger/stick as its value.
                sb.Append("km.controller(").Append(id).Append(',').Append(value[id]).Append(")\n");
            }

            long now = clock.ElapsedMilliseconds;
            if (sb.Length == 0)
            {
                if (now - lastSendMs < PingMs) return;
                sb.Append("km.version()\n");   // keep-alive for the other side's idle timeout
            }

            if (link.TrySend(sb.ToString()))
            {
                Array.Copy(value, sent, value.Length);
                lastSendMs = now;
                if (all) resendAll = false;
            }
        }
    }
}
