using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GamepadEnhancer
{
    /// <summary>The calls the engine makes on an Xbox 360 pad; implemented by the real ViGEm pad and by the network sender.</summary>
    internal interface IXboxPad
    {
        void Connect();
        void Disconnect();
        void SetAxisValue(Xbox360Axis axis, short value);
        void SetButtonState(Xbox360Button button, bool pressed);
        void SetSliderValue(Xbox360Slider slider, byte value);
        void SubmitReport();
    }

    /// <summary>The calls the engine makes on a DualShock 4 pad.</summary>
    internal interface IDs4Pad
    {
        void Connect();
        void Disconnect();
        void SetAxisValue(DualShock4Axis axis, byte value);
        void SetButtonState(DualShock4Button button, bool pressed);
        /// <summary>The PS button (and touchpad click).</summary>
        void SetButtonState(DualShock4SpecialButton button, bool pressed);
        void SetSliderValue(DualShock4Slider slider, byte value);
        void SetDPadDirection(DualShock4DPadDirection direction);
        void SubmitReport();
    }

    /// <summary>Optional: a pad that needs a little service on every engine tick (the VHF pad reads the driver's feedback and recovers).</summary>
    internal interface IPadPump
    {
        void Pump();
    }

    internal sealed class ViGEmXboxPad : IXboxPad
    {
        private readonly IXbox360Controller pad;
        public ViGEmXboxPad(ViGEmClient client) { pad = client.CreateXbox360Controller(); pad.AutoSubmitReport = false; }
        public void Connect() => pad.Connect();
        public void Disconnect() => pad.Disconnect();
        public void SetAxisValue(Xbox360Axis axis, short value) => pad.SetAxisValue(axis, value);
        public void SetButtonState(Xbox360Button button, bool pressed) => pad.SetButtonState(button, pressed);
        public void SetSliderValue(Xbox360Slider slider, byte value) => pad.SetSliderValue(slider, value);
        public void SubmitReport() => pad.SubmitReport();
    }

    internal sealed class ViGEmDs4Pad : IDs4Pad
    {
        private readonly IDualShock4Controller pad;
        public ViGEmDs4Pad(ViGEmClient client) { pad = client.CreateDualShock4Controller(); pad.AutoSubmitReport = false; }
        public void Connect() => pad.Connect();
        public void Disconnect() => pad.Disconnect();
        public void SetAxisValue(DualShock4Axis axis, byte value) => pad.SetAxisValue(axis, value);
        public void SetButtonState(DualShock4Button button, bool pressed) => pad.SetButtonState(button, pressed);
        public void SetButtonState(DualShock4SpecialButton button, bool pressed) => pad.SetButtonState(button, pressed);
        public void SetSliderValue(DualShock4Slider slider, byte value) => pad.SetSliderValue(slider, value);
        public void SetDPadDirection(DualShock4DPadDirection direction) => pad.SetDPadDirection(direction);
        public void SubmitReport() => pad.SubmitReport();
    }

    /// <summary>Where to send the pad when the engine runs in "remote PC" mode.</summary>
    internal sealed class RemoteTarget
    {
        public RemoteTarget(string host, int port, string token) { Host = host; Port = port; Token = token; }
        public string Host { get; }
        public int Port { get; }
        public string Token { get; }
    }
}
