using System;

namespace GamepadEnhancer
{
    /// <summary>Pure axis maths shared by the engine (and unit-testable without ViGEm).</summary>
    public static class StickMath
    {
        public const byte Ds4Center = 128;

        public static int ClampPercent(int value) => Math.Clamp(value, -100, 100);

        public static short PercentToXboxAxis(int percent) => PercentToXboxAxis((double)percent);

        public static byte PercentToDs4Axis(int percent) => PercentToDs4Axis((double)percent);

        /// <summary>16-bit Xbox axis for a percent in -100..100 (fractional percents allowed, e.g. mouse look).</summary>
        public static short PercentToXboxAxis(double percent)
            => (short)Math.Round(Math.Clamp(percent, -100.0, 100.0) * (short.MaxValue / 100.0));

        /// <summary>8-bit DS4 axis (center 128) for a percent in -100..100 (fractional percents allowed).</summary>
        public static byte PercentToDs4Axis(double percent)
            => (byte)Math.Clamp((int)Math.Round(128 + (Math.Clamp(percent, -100.0, 100.0) * 127.0 / 100.0)), 0, 255);
    }
}
