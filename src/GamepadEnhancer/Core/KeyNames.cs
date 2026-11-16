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
    }
}
