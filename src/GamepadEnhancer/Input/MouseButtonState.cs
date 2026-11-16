using System;
using System.Runtime.InteropServices;

namespace GamepadEnhancer
{
    /// <summary>Physical mouse button state via GetAsyncKeyState (accounts for swapped primary/secondary buttons).</summary>
    internal static class MouseButtonState
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

        internal const int VkLeft = 0x01, VkRight = 0x02, VkMiddle = 0x04, VkX1 = 0x05, VkX2 = 0x06;

        internal static bool IsSwapped() => OperatingSystem.IsWindows() && GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0;

        /// <summary>Maps the logical left/right button to the physical VK given the swap setting.</summary>
        internal static int ResolveVk(int vk, bool swapped)
        {
            if (!swapped) return vk;
            return vk == VkLeft ? VkRight : vk == VkRight ? VkLeft : vk;
        }

        internal static bool IsDown(int vk) =>
            OperatingSystem.IsWindows() && (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>Physical button mask for the Network API's km.mouse: bit0 left, bit1 right, bit2 middle, bit3 X1, bit4 X2.</summary>
        internal static byte ReadMask()
        {
            bool swapped = IsSwapped();
            byte mask = 0;
            if (IsDown(ResolveVk(VkLeft, swapped))) mask |= 0x01;
            if (IsDown(ResolveVk(VkRight, swapped))) mask |= 0x02;
            if (IsDown(VkMiddle)) mask |= 0x04;
            if (IsDown(VkX1)) mask |= 0x08;
            if (IsDown(VkX2)) mask |= 0x10;
            return mask;
        }
    }
}
