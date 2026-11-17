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

        /// <summary>Mouse movement drives the right stick (see <see cref="MouseLook"/>). Off by default.</summary>
        public bool MouseLookEnabled { get; init; }
        /// <summary>Percent of stick per (count/ms) horizontally / vertically.</summary>
        public double MouseSensitivityX { get; init; } = 8.0;
        public double MouseSensitivityY { get; init; } = 8.0;
        /// <summary>Response exponent: 1 = linear, above 1 = finer at low speed.</summary>
        public double MouseCurve { get; init; } = 1.0;
        /// <summary>The game's inner dead zone in percent (0..50); mouse output starts just past it.</summary>
        public int MouseGameDeadZonePercent { get; init; }
        /// <summary>How long the last mouse output is held with no movement before the stick recenters.</summary>
        public int MouseReleaseMs { get; init; } = 30;
        public bool MouseInvertY { get; init; }
        /// <summary>The four rear paddles (slot order of <see cref="PaddleSlots"/>); empty = none configured.</summary>
        public PaddleSetting[] Paddles { get; init; } = Array.Empty<PaddleSetting>();
        /// <summary>Hardware paddle index (0..3) feeding each slot; null = 0,1,2,3.</summary>
        public int[] PaddleSourceIndex { get; init; }
        public Dictionary<string, BindingSetting> Bindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
