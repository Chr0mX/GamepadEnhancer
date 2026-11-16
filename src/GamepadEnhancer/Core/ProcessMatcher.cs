using System;
using System.Collections.Generic;

namespace GamepadEnhancer
{
    /// <summary>Matches a foreground process name against a user-entered list ("game.exe, other"). Pure.</summary>
    internal static class ProcessMatcher
    {
        /// <summary>Splits on comma, semicolon and newlines; trims; drops a trailing ".exe"; lower-cases.</summary>
        public static List<string> ParseList(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = Normalize(part);
                if (n.Length > 0 && !result.Contains(n)) result.Add(n);
            }
            return result;
        }

        public static string Normalize(string name)
        {
            string n = (name ?? "").Trim().Trim('"');
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.ToLowerInvariant();
        }

        public static bool Matches(string processName, IReadOnlyCollection<string> normalizedList)
        {
            if (normalizedList == null || normalizedList.Count == 0) return false;
            string n = Normalize(processName);
            if (n.Length == 0) return false;
            foreach (string item in normalizedList) if (item == n) return true;
            return false;
        }
    }
}
