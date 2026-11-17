using System;
using System.Collections.Generic;
using System.Globalization;

namespace GamepadEnhancer
{
    /// <summary>Maps Windows virtual-key codes to the key names used in profiles and the UI.</summary>
    public static class KeyNames
    {
        /// <summary>Keys with a fixed name (everything except the modifiers, digits, letters and F-keys, which are handled by rule).</summary>
        private static readonly (string Name, int Vk)[] Named =
        {
            ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B), ("Backspace", 0x08), ("Tab", 0x09),
            ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28), ("Home", 0x24),
            ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22), ("Insert", 0x2D), ("Delete", 0x2E),
            ("CapsLock", 0x14), ("Pause", 0x13), ("PrintScreen", 0x2C), ("NumLock", 0x90),
            ("Numpad0", 0x60), ("Numpad1", 0x61), ("Numpad2", 0x62), ("Numpad3", 0x63), ("Numpad4", 0x64),
            ("Numpad5", 0x65), ("Numpad6", 0x66), ("Numpad7", 0x67), ("Numpad8", 0x68), ("Numpad9", 0x69),
            ("NumpadMultiply", 0x6A), ("NumpadAdd", 0x6B), ("NumpadSubtract", 0x6D), ("NumpadDecimal", 0x6E), ("NumpadDivide", 0x6F),
            ("Minus", 0xBD), ("Equals", 0xBB), ("LeftBracket", 0xDB), ("RightBracket", 0xDD), ("Backslash", 0xDC),
            ("Semicolon", 0xBA), ("Quote", 0xDE), ("Comma", 0xBC), ("Period", 0xBE), ("Slash", 0xBF), ("Backtick", 0xC0),
        };

        private static readonly Dictionary<int, string> ByVk = BuildByVk();
        private static readonly Dictionary<string, int> ByName = BuildByName();

        private static Dictionary<int, string> BuildByVk()
        {
            var d = new Dictionary<int, string>();
            foreach (var (name, vk) in Named) d[vk] = name;
            return d;
        }

        private static Dictionary<string, int> BuildByName()
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (name, vk) in Named) d[name] = vk;
            return d;
        }

        /// <summary>All the fixed names, in the order they are offered in pickers.</summary>
        internal static IEnumerable<string> ExtraNames()
        {
            foreach (var (name, _) in Named) yield return name;
        }

        public static string FromVirtualKey(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
            }
            if (ByVk.TryGetValue(vk, out string named)) return named;
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
            return "VK" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reverse of <see cref="FromVirtualKey"/>: every virtual-key code that maps to <paramref name="name"/>
        /// (for example "Shift" is 0x10, 0xA0 and 0xA1). Unknown names give an empty array.
        /// </summary>
        public static int[] ToVirtualKeys(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return Array.Empty<int>();
            switch (name)
            {
                case "Shift": return new[] { 0x10, 0xA0, 0xA1 };
                case "Ctrl": return new[] { 0x11, 0xA2, 0xA3 };
                case "Alt": return new[] { 0x12, 0xA4, 0xA5 };
            }
            if (ByName.TryGetValue(name, out int fixedVk)) return new[] { fixedVk };
            if (name.Length == 1 && ((name[0] >= '0' && name[0] <= '9') || (name[0] >= 'A' && name[0] <= 'Z'))) return new[] { (int)name[0] };
            if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
                return new[] { 0x6F + f };
            if (name.Length > 2 && name.StartsWith("VK", StringComparison.Ordinal)
                && int.TryParse(name.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk))
                return new[] { vk };
            return Array.Empty<int>();
        }
    }
}
