using System;
using Windows.Gaming.Input;

namespace GamepadEnhancer
{
    /// <summary>
    /// Reads the physical controller's rear paddles through Windows.Gaming.Input (<c>GamepadButtons.Paddle1..Paddle4</c>, which Windows
    /// reports for the Xbox Elite controllers). Windows.Gaming.Input only lists XInput-class gamepads; controllers that expose their
    /// back buttons any other way (for example the DualSense Edge) are not visible here. Reading is a poll, done by the engine once per
    /// tick and by the UI for its status line. Never throws: any failure reads as "no controller".
    /// </summary>
    internal sealed class WgiPaddleSource : IPaddleSource
    {
        public PaddleReading Read()
        {
            try
            {
                var pads = Gamepad.Gamepads;
                if (pads == null || pads.Count == 0) return PaddleReading.None;

                Gamepad chosen = null;
                RawGameController chosenRaw = null;
                for (int i = 0; i < pads.Count; i++)
                {
                    Gamepad g = pads[i];
                    RawGameController raw = null;
                    try { raw = RawGameController.FromGameController(g); } catch { }
                    if (chosen == null) { chosen = g; chosenRaw = raw; }
                    if (raw != null && PaddleCaps.KnownPaddleCount(raw.HardwareVendorId, raw.HardwareProductId) > 0)
                    {
                        chosen = g; chosenRaw = raw;   // prefer a controller that is known to have paddles
                        break;
                    }
                }

                GamepadButtons b = chosen.GetCurrentReading().Buttons;
                int mask = ((b & GamepadButtons.Paddle1) != 0 ? 1 : 0)
                         | ((b & GamepadButtons.Paddle2) != 0 ? 2 : 0)
                         | ((b & GamepadButtons.Paddle3) != 0 ? 4 : 0)
                         | ((b & GamepadButtons.Paddle4) != 0 ? 8 : 0);
                return new PaddleReading(true, mask, chosenRaw?.DisplayName ?? "Gamepad",
                    chosenRaw != null ? chosenRaw.HardwareVendorId : 0, chosenRaw != null ? chosenRaw.HardwareProductId : 0);
            }
            catch
            {
                return PaddleReading.None;
            }
        }
    }
}
