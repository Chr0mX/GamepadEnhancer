using System;
using System.Collections.Generic;

namespace GamepadEnhancer
{
    public sealed class BindingSetting
    {
        public string Key { get; init; } = "None";
        public bool Turbo { get; init; }
        public int TurboHz { get; init; } = 12;
    }

    /// <summary>Immutable snapshot of everything the engine thread needs from the UI.</summary>
    public sealed class EngineSettings
    {
        public static readonly EngineSettings Default = new();

        /// <summary>Base downward drift percent (0..100), as in the original app.</summary>
        public int DriftValue { get; init; }
        /// <summary>Right-stick jitter/filter: 0 = off, negative = amplitude in raw counts (e.g. -10).</summary>
        public int JitterValue { get; init; }
        /// <summary>"Oscillation" (RC-style micro-oscillation, Y only) or "LowPass" (exponential low-pass, X and Y).</summary>
        public string JitterAlgorithm { get; init; } = "Oscillation";
        /// <summary>Button that switches the RS filtering on: "Mouse Left"/"Mouse Right"/"Mouse Middle", or "None" = always on.</summary>
        public string JitterTriggerKey { get; init; } = "None";
        /// <summary>false = Hold (filtering only while the button is held), true = Toggle (each press flips it).</summary>
        public bool JitterToggleMode { get; init; }
        /// <summary>
        /// Output period (ms) while filtering is active: 2 (500/s, default), 4, 8 or 16. A lower rate means fewer pad
        /// events for the game to see; some games flip between controller and mouse less. Experimental.
        /// </summary>
        public int FilterPeriodMs { get; init; } = 2;
        public string PullDirection { get; init; } = "Down";   // Down / Up / Left / Right ("Center" is read as Down)
        public bool RecoilEnabled { get; init; }
        /// <summary>When true the pattern restarts from step 1 after the last step instead of holding it.</summary>
        public bool RecoilLoop { get; init; }
        /// <summary>Aim trigger: "Mouse Left", "Mouse Right", "Mouse Middle" or "None" (off).</summary>
        public string RecoilTriggerKey { get; init; } = "Mouse Left";
        /// <summary>false = Hold (active while pressed), true = Toggle (each press flips on/off).</summary>
        public bool RecoilToggleMode { get; init; }
        /// <summary>Delay after the trigger activates before the pattern starts.</summary>
        public int RecoilStartDelayMs { get; init; }
        /// <summary>Minimum stick deflection (percent) for non-zero pattern steps.</summary>
        public int RecoilCenterRadius { get; init; }
        /// <summary>
        /// How Center Radius is used: "Lift" = new dead-zone lift, "Shake" / "Circle" = old centered wobble.
        /// See docs/CENTER_RADIUS.md.
        /// </summary>
        public string RecoilCenterRadiusMode { get; init; } = "Lift";
        public RecoilStep[] RecoilSteps { get; init; } = Array.Empty<RecoilStep>();
        public bool WheelDisengageEnabled { get; init; } = true;
        public int DisengageMs { get; init; } = 500;
        public HashSet<string> DisableKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BindingSetting> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
