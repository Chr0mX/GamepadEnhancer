using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>Parsing / formatting for recoil patterns (inline text and .config files).</summary>
    public static class RecoilPattern
    {
        public const string DefaultText = "0:0:17.3|0:18:17.3|2:32:17.3|4:44:17.3|5:50:17.3";

        public static List<RecoilStep> ParseText(string text)
        {
            var steps = new List<RecoilStep>();
            if (string.IsNullOrWhiteSpace(text)) return steps;
            foreach (string segment in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = segment.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;
                double delay = 80;
                if (parts.Length >= 3) double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out delay);
                steps.Add(Make(x, y, delay));
            }
            return steps;
        }

        public static string ToText(IEnumerable<RecoilStep> steps)
            => string.Join("|", steps.Select(s => $"{s.X}:{s.Y}:{Fmt(s.IntervalMs)}"));

        public static List<RecoilStep> ParseFileLines(IEnumerable<string> lines)
        {
            var steps = new List<RecoilStep>();
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 3) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double delay)) continue;
                steps.Add(Make(x, y, delay));
            }
            return steps;
        }

        public static IEnumerable<string> ToFileLines(IEnumerable<RecoilStep> steps)
            => steps.Select(s => $"{s.X},{s.Y},{Fmt(s.IntervalMs)}");

        public static List<RecoilStep> VerticalPreset() => new()
        {
            Make(0, 0, 17.3), Make(0, 18, 17.3), Make(2, 32, 17.3), Make(4, 44, 17.3),
            Make(5, 50, 17.3), Make(4, 48, 17.3), Make(2, 44, 17.3)
        };

        /// <summary>Tight circular motion around the stick center: 16 steps, radius 8 %, 17.3 ms each (use with Loop).</summary>
        public static List<RecoilStep> CirclePreset()
        {
            const int count = 16;
            const int radius = 8;
            var steps = new List<RecoilStep>(count);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2 * i / count;
                steps.Add(Make((int)Math.Round(Math.Cos(angle) * radius), (int)Math.Round(Math.Sin(angle) * radius), 17.3));
            }
            return steps;
        }

        public static RecoilStep Make(int x, int y, double delay)
            => new() { X = StickMath.ClampPercent(x), Y = StickMath.ClampPercent(y), IntervalMs = Math.Max(0.1, delay) };

        private static string Fmt(double v) => v.ToString("G", CultureInfo.InvariantCulture);
    }
}
