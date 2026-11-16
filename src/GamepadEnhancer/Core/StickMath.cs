using System;

namespace GamepadEnhancer
{
    /// <summary>Pure axis maths shared by the engine (and unit-testable without ViGEm).</summary>
    public static class StickMath
    {
        /// <summary>Base drift slider limits (percent). Base drift is never negative.</summary>
        public const int MinDrift = 0;
        public const int MaxDrift = 100;
        /// <summary>Right-stick jitter/filter slider limits. 0 = off, negative = amplitude in raw axis counts.</summary>
        public const int MinJitter = -50;
        public const int MaxJitter = 0;

        public const byte Ds4Center = 128;
        public static int ClampPercent(int value) => Math.Clamp(value, -100, 100);

        public static short PercentToXboxAxis(int percent) => PercentToXboxAxis((double)percent);

        public static byte PercentToDs4Axis(int percent) => PercentToDs4Axis((double)percent);

        /// <summary>16-bit Xbox axis for a percent in -100..100 (fractional percents allowed, e.g. filter output).</summary>
        public static short PercentToXboxAxis(double percent)
            => (short)Math.Round(Math.Clamp(percent, -100.0, 100.0) * (short.MaxValue / 100.0));

        /// <summary>8-bit DS4 axis (center 128) for a percent in -100..100 (fractional percents allowed).</summary>
        public static byte PercentToDs4Axis(double percent)
            => (byte)Math.Clamp((int)Math.Round(128 + (Math.Clamp(percent, -100.0, 100.0) * 127.0 / 100.0)), 0, 255);

        /// <summary>Jitter amplitude in raw DS4 axis counts for a slider value (0 when not in jitter mode).</summary>
        public static int JitterAmplitude(int sliderValue) => sliderValue < 0 ? -sliderValue : 0;

        /// <summary>
        /// Center radius: a non-zero pattern vector shorter than <paramref name="radiusPercent"/> is pushed out to that
        /// length (same direction) so small steps clear the game's inner dead zone. (0,0) stays at the center.
        /// </summary>
        public static (int X, int Y) ApplyCenterRadius(int x, int y, int radiusPercent)
        {
            if (radiusPercent <= 0 || (x == 0 && y == 0)) return (x, y);
            double mag = Math.Sqrt((double)x * x + (double)y * y);
            if (mag >= radiusPercent) return (x, y);
            double k = radiusPercent / mag;
            return (ClampPercent((int)Math.Round(x * k)), ClampPercent((int)Math.Round(y * k)));
        }

        /// <summary>
        /// RC-style micro-oscillation, DS4 (works on any axis value, the engine picks the axis perpendicular to the drift): the output alternates between target + A and target - A, where A is the
        /// amplitude shrunk to what fits on both sides of the target, so it is exactly symmetric (no directional bias)
        /// even near the stick limits.
        /// </summary>
        public static byte Ds4OscillateY(byte targetY, int amplitude, bool highPhase)
        {
            int a = Math.Min(Math.Max(0, amplitude), Math.Min(targetY, 255 - targetY));
            return (byte)(highPhase ? targetY + a : targetY - a);
        }

        /// <summary>Xbox variant; the amplitude (8-bit counts) is scaled to 16 bits, then limited to stay symmetric.</summary>
        public static short XboxOscillateY(short targetY, int amplitude, bool highPhase)
        {
            int raw = (int)Math.Round(Math.Max(0, amplitude) * (short.MaxValue / 127.0));
            int a = Math.Min(raw, Math.Min(short.MaxValue - targetY, targetY - short.MinValue));
            return (short)(highPhase ? targetY + a : targetY - a);
        }

        /// <summary>Low-pass time constant (ms) for the slider value: 4 ms per step, so -50 = 200 ms; 0 = no smoothing.</summary>
        public static double SmoothingTauMs(int sliderValue) => sliderValue < 0 ? -sliderValue * 4.0 : 0.0;

        /// <summary>
        /// Soft wiggle (Exponential Low-Pass mode): one wave every 16 ms x N, i.e. the square-wave half-period is 2 x tau
        /// (tau = 4 ms x N), so the low-pass rounds each flip instead of erasing it. Tune the 16 here.
        /// </summary>
        public const double SoftWigglePeriodMsPerStep = 16.0;

        public static double SoftWigglePeriodMs(int sliderValue) => sliderValue < 0 ? -sliderValue * SoftWigglePeriodMsPerStep : 0.0;

        /// <summary>
        /// Square-wave drive (percent) that the low-pass turns into a wiggle whose peak is +/-N counts: a symmetric square
        /// wave with half-period 2 x tau settles to a peak of A x tanh(1), so A = N / tanh(1). One count is 100/127 percent
        /// on both DS4 and Xbox. Shrunk near the stick limit so target +/- A stays in range (stays symmetric).
        /// </summary>
        public static double SoftWiggleDrivePercent(int sliderValue, double targetPercent)
        {
            int n = JitterAmplitude(sliderValue);
            if (n <= 0) return 0.0;
            double a = n * (100.0 / 127.0) / Math.Tanh(1.0);
            return Math.Max(0.0, Math.Min(a, 100.0 - Math.Abs(targetPercent)));
        }

        /// <summary>+1 / -1 square wave taken from the clock, so the wiggle timing does not depend on the tick rate.</summary>
        public static int SquareWave(double nowMs, double periodMs)
            => periodMs <= 0 ? 0 : (Math.Floor(nowMs / (periodMs / 2.0)) % 2 == 0 ? 1 : -1);

        /// <summary>Frame-rate independent smoothing factor: 1 - exp(-dt / tau). tau &lt;= 0 means "no smoothing" (1).</summary>
        public static double LowPassAlpha(double tauMs, double dtMs)
        {
            if (tauMs <= 0) return 1.0;
            if (dtMs <= 0) return 0.0;
            return 1.0 - Math.Exp(-dtMs / tauMs);
        }
    }
}
