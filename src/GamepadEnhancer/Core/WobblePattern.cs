using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>
    /// Centered Shake / Circle wobble used by the "old" Center Radius mode.
    /// Ported unchanged from the pre-removal jitter code (git: b69b6c1^, MainForm.cs
    /// BuildShakePattern / BuildCirclePattern / CenterAndScalePattern). See docs/CENTER_RADIUS.md.
    /// </summary>
    public static class WobblePattern
    {
        public const int MaxRadius = 45;
        /// <summary>The old code advanced one wobble step per 10 ms timer tick; this keeps that speed.</summary>
        public const int StepIntervalMs = 10;

        public static (int X, int Y)[] Build(string mode, int radius)
        {
            radius = Math.Clamp(radius, 0, MaxRadius);
            if (radius <= 0) return Array.Empty<(int, int)>();

            List<(int X, int Y)> pattern = string.Equals(mode, "Circle", StringComparison.OrdinalIgnoreCase)
                ? BuildCirclePattern(radius)
                : BuildShakePattern(radius);
            return CenterAndScalePattern(pattern, radius).ToArray();
        }

        public static List<(int X, int Y)> BuildShakePattern(int radius)
        {
            int wide = Math.Max(4, radius);
            int tight = Math.Max(2, (int)Math.Round(radius * 0.45));
            return new List<(int X, int Y)>
            {
                (-wide, 0),
                (wide, 0),
                (-tight, tight),
                (tight, -tight)
            };
        }

        public static List<(int X, int Y)> BuildCirclePattern(int radius)
        {
            var steps = new List<(int X, int Y)>();
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                double angle = (Math.PI * 2 * i) / count;
                steps.Add((
                    StickMath.ClampPercent((int)Math.Round(Math.Cos(angle) * radius)),
                    StickMath.ClampPercent((int)Math.Round(Math.Sin(angle) * radius))));
            }
            return steps;
        }

        public static List<(int X, int Y)> CenterAndScalePattern(List<(int X, int Y)> steps, int radius)
        {
            if (steps.Count == 0) return new List<(int X, int Y)>();

            double averageX = steps.Average(s => s.X);
            double averageY = steps.Average(s => s.Y);

            var centered = steps
                .Select(s => (X: s.X - (int)Math.Round(averageX), Y: s.Y - (int)Math.Round(averageY)))
                .ToList();

            int maxMagnitude = centered
                .Select(s => Math.Max(Math.Abs(s.X), Math.Abs(s.Y)))
                .DefaultIfEmpty(0)
                .Max();

            if (maxMagnitude <= 0 || radius <= 0) return centered;

            double scale = radius / (double)maxMagnitude;
            return centered
                .Select(s => (
                    X: StickMath.ClampPercent((int)Math.Round(s.X * scale)),
                    Y: StickMath.ClampPercent((int)Math.Round(s.Y * scale))))
                .ToList();
        }
    }
}
