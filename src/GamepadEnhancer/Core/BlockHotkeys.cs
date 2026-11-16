using System;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>The keys offered for the block toggle / panic hotkey. Pure.</summary>
    internal static class BlockHotkeys
    {
        public const int DefaultVk = 0x91;   // Scroll Lock

        private static readonly (string Name, int Vk)[] All =
        {
            ("Scroll Lock", 0x91), ("Pause", 0x13), ("Insert", 0x2D), ("Delete", 0x2E),
            ("Home", 0x24), ("End", 0x23), ("Page Up", 0x21), ("Page Down", 0x22),
            ("F10", 0x79), ("F11", 0x7A), ("F12", 0x7B)
        };

        public static string[] Names => All.Select(a => a.Name).ToArray();

        public static int ToVk(string name)
        {
            foreach (var a in All) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a.Vk;
            return DefaultVk;
        }

        public static string FromVk(int vk)
        {
            foreach (var a in All) if (a.Vk == vk) return a.Name;
            return All[0].Name;
        }
    }
}
