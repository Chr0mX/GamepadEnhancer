using System.Collections.Generic;
using System.Globalization;

namespace GamepadEnhancer
{
    /// <summary>Maps Windows virtual-key codes to the key names used in profiles and the UI.</summary>
    public static class KeyNames
    {
        public static string FromVirtualKey(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
                case 0x20: return "Space";
                case 0x0D: return "Enter";
                case 0x1B: return "Escape";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
            }
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
            return "VK" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reverse of <see cref="FromVirtualKey"/>: every virtual-key code that maps to <paramref name="name"/>
        /// (for example "Shift" is 0x10, 0xA0 and 0xA1). Unknown names give an empty array.
        /// </summary>
        public static int[] ToVirtualKeys(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", System.StringComparison.OrdinalIgnoreCase)) return System.Array.Empty<int>();
            switch (name)
            {
                case "Shift": return new[] { 0x10, 0xA0, 0xA1 };
                case "Ctrl": return new[] { 0x11, 0xA2, 0xA3 };
                case "Alt": return new[] { 0x12, 0xA4, 0xA5 };
                case "Space": return new[] { 0x20 };
                case "Enter": return new[] { 0x0D };
                case "Escape": return new[] { 0x1B };
                case "Backspace": return new[] { 0x08 };
                case "Tab": return new[] { 0x09 };
                case "Left": return new[] { 0x25 };
                case "Up": return new[] { 0x26 };
                case "Right": return new[] { 0x27 };
                case "Down": return new[] { 0x28 };
            }
            if (name.Length == 1 && ((name[0] >= '0' && name[0] <= '9') || (name[0] >= 'A' && name[0] <= 'Z'))) return new[] { (int)name[0] };
            if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 12)
                return new[] { 0x6F + f };
            if (name.Length > 2 && name.StartsWith("VK", System.StringComparison.Ordinal)
                && int.TryParse(name.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk))
                return new[] { vk };
            return System.Array.Empty<int>();
        }
    }
}
