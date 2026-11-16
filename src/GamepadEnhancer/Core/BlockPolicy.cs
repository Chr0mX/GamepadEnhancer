using System;
using System.Collections.Generic;

namespace GamepadEnhancer
{
    /// <summary>Immutable snapshot of what the input blocker should swallow. Built on the UI thread, read by the hook callbacks.</summary>
    internal sealed class BlockSettings
    {
        public static readonly BlockSettings Off = new();

        /// <summary>Master switch: enabled AND toggled on AND the target window is focused.</summary>
        public bool Active { get; init; }
        /// <summary>Virtual-key codes (as reported by the keyboard hook) of keys that are mapped to the controller.</summary>
        public HashSet<int> BlockedKeys { get; init; } = new();
        /// <summary>"Mouse Left" / "Mouse Right" / "Mouse Middle" / "Mouse X1" / "Mouse X2" / "Wheel Up" / "Wheel Down" that are mapped.</summary>
        public HashSet<string> BlockedMouseInputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Swallow mouse movement (mouse-look drives the right stick).</summary>
        public bool BlockMouseMove { get; init; }
        /// <summary>Virtual-key code of the toggle/panic hotkey. It is never blocked. 0 = none.</summary>
        public int HotkeyVk { get; init; }

        /// <summary>
        /// Builds the blocked set from what the profile maps: keys bound to a pad action, mouse buttons/wheel bound to a
        /// pad action, and mouse movement when mouse-look is on. Unmapped inputs are never blocked.
        /// </summary>
        public static BlockSettings From(EngineSettings engine, bool active, int hotkeyVk)
        {
            var keys = new HashSet<int>();
            var mouse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (engine != null)
            {
                foreach (BindingSetting b in engine.Bindings.Values)
                {
                    if (b == null || string.IsNullOrWhiteSpace(b.Key) || b.Key.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                    if (MouseInputNames.IsMouseInput(b.Key)) mouse.Add(b.Key);
                    else foreach (int vk in KeyNames.ToVirtualKeys(b.Key)) keys.Add(vk);
                }
            }
            return new BlockSettings
            {
                Active = active,
                BlockedKeys = keys,
                BlockedMouseInputs = mouse,
                BlockMouseMove = engine != null && engine.MouseLookEnabled,
                HotkeyVk = hotkeyVk
            };
        }
    }

    /// <summary>Names used for mouse inputs that can be bound to pad actions.</summary>
    internal static class MouseInputNames
    {
        public const string Left = "Mouse Left", Right = "Mouse Right", Middle = "Mouse Middle", X1 = "Mouse X1", X2 = "Mouse X2";
        public const string WheelUp = "Wheel Up", WheelDown = "Wheel Down";

        public static readonly string[] All = { Left, Right, Middle, X1, X2, WheelUp, WheelDown };

        public static bool IsMouseInput(string name)
        {
            foreach (string n in All) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>
    /// Decides, per physical event, whether the low-level hook should swallow it. Pure and Win32-free.
    /// Rules: injected events and the hotkey are never blocked; an "up" is blocked only if its "down" was blocked
    /// (and a repeat "down" of a key we already swallowed is swallowed too), so the target app never sees a stuck key.
    /// The pairing state is only touched from the hook thread; the settings snapshot may be swapped from any thread.
    /// </summary>
    internal sealed class BlockPolicy
    {
        private volatile BlockSettings settings = BlockSettings.Off;
        private readonly HashSet<int> swallowedKeyDowns = new();
        private readonly HashSet<string> swallowedMouseDowns = new(StringComparer.OrdinalIgnoreCase);

        public BlockSettings Settings => settings;
        public bool Active => settings.Active;
        public bool BlocksMouseMove => settings.Active && settings.BlockMouseMove;

        public void Update(BlockSettings next) => settings = next ?? BlockSettings.Off;

        public bool IsHotkey(int vk) => vk != 0 && vk == settings.HotkeyVk;

        public bool DecideKey(int vk, bool isDown, bool injected)
        {
            if (injected || IsHotkey(vk)) return false;
            if (isDown)
            {
                if (swallowedKeyDowns.Contains(vk)) return true;                       // auto-repeat of a swallowed key
                BlockSettings s = settings;
                if (s.Active && s.BlockedKeys.Contains(vk)) { swallowedKeyDowns.Add(vk); return true; }
                return false;
            }
            return swallowedKeyDowns.Remove(vk);                                          // up of a swallowed down; otherwise pass
        }

        public bool DecideMouseButton(string button, bool isDown, bool injected)
        {
            if (injected) return false;
            if (isDown)
            {
                if (swallowedMouseDowns.Contains(button)) return true;
                BlockSettings s = settings;
                if (s.Active && s.BlockedMouseInputs.Contains(button)) { swallowedMouseDowns.Add(button); return true; }
                return false;
            }
            return swallowedMouseDowns.Remove(button);
        }

        /// <summary>Wheel notches have no "up", so they are decided per notch.</summary>
        public bool DecideWheel(bool up, bool injected)
        {
            if (injected) return false;
            BlockSettings s = settings;
            return s.Active && s.BlockedMouseInputs.Contains(up ? MouseInputNames.WheelUp : MouseInputNames.WheelDown);
        }

        public bool DecideMouseMove(bool injected) => !injected && BlocksMouseMove;

        /// <summary>Forgets pairing state (call when the hook stops).</summary>
        public void Reset()
        {
            swallowedKeyDowns.Clear();
            swallowedMouseDowns.Clear();
        }
    }
}
