using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>What kind of physical input a binding uses. Written to profiles for readers; derived from the input name on load.</summary>
    internal enum InputKind { None, Keyboard, MouseButton, MouseWheel }

    /// <summary>
    /// The inputs a pad action can be bound to, in three clearly separate groups: keyboard keys, mouse buttons and the
    /// mouse wheel. (Mouse <em>movement</em> is not a binding: it is the right stick, see mouse look.) Pure, so it can be
    /// unit tested and used to validate imported profiles. The stored names are the ones profiles have always used.
    /// </summary>
    internal static class InputChoices
    {
        public const string None = "None";

        public static readonly string[] Keyboard = BuildKeyboard();

        public static readonly string[] MouseButtons =
        {
            MouseInputNames.Left, MouseInputNames.Right, MouseInputNames.Middle, MouseInputNames.X1, MouseInputNames.X2
        };

        public static readonly string[] MouseWheel = { MouseInputNames.WheelUp, MouseInputNames.WheelDown };

        private static string[] BuildKeyboard()
        {
            var keys = new List<string>
            {
                "Shift", "Ctrl", "Alt", "Space", "Tab", "Enter", "Escape", "Backspace", "Home",
                "Up", "Down", "Left", "Right"
            };
            keys.AddRange(Enumerable.Range(0, 10).Select(i => i.ToString(CultureInfo.InvariantCulture)));
            for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
            for (int i = 1; i <= 12; i++) keys.Add("F" + i.ToString(CultureInfo.InvariantCulture));
            return keys.ToArray();
        }

        public static InputKind KindOf(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Equals(None, StringComparison.OrdinalIgnoreCase)) return InputKind.None;
            if (MouseButtons.Contains(input, StringComparer.OrdinalIgnoreCase)) return InputKind.MouseButton;
            if (MouseWheel.Contains(input, StringComparer.OrdinalIgnoreCase)) return InputKind.MouseWheel;
            if (Keyboard.Contains(input, StringComparer.OrdinalIgnoreCase)) return InputKind.Keyboard;
            return InputKind.None;
        }

        /// <summary>True for the keyboard-driven left stick directions: those take keyboard keys only.</summary>
        public static bool IsStickAction(string action) =>
            action == ControllerEngine.ActionLeftStickUp || action == ControllerEngine.ActionLeftStickDown
            || action == ControllerEngine.ActionLeftStickLeft || action == ControllerEngine.ActionLeftStickRight;

        /// <summary>Whether <paramref name="input"/> may be bound to <paramref name="action"/> (stick directions: keyboard only).</summary>
        public static bool IsAllowed(string action, string input)
        {
            InputKind kind = KindOf(input);
            if (kind == InputKind.None) return true;
            return !IsStickAction(action) || kind == InputKind.Keyboard;
        }

        /// <summary>The stored name with its canonical casing, or "None" when it is not a known input.</summary>
        public static string Canonical(string input)
        {
            if (KindOf(input) == InputKind.None) return None;
            foreach (string[] group in new[] { Keyboard, MouseButtons, MouseWheel })
                foreach (string name in group)
                    if (name.Equals(input, StringComparison.OrdinalIgnoreCase)) return name;
            return None;
        }

        public static string KindLabel(InputKind kind) => kind switch
        {
            InputKind.Keyboard => "Keyboard",
            InputKind.MouseButton => "Mouse button",
            InputKind.MouseWheel => "Mouse wheel",
            _ => "Not set"
        };

        /// <summary>Friendly picker text for a stored input name (the stored name itself never changes).</summary>
        public static string DisplayName(string input) => input switch
        {
            MouseInputNames.Left => "Mouse 1 (Left)",
            MouseInputNames.Right => "Mouse 2 (Right)",
            MouseInputNames.Middle => "Mouse 3 (Middle)",
            MouseInputNames.X1 => "Mouse 4 (Side, back)",
            MouseInputNames.X2 => "Mouse 5 (Side, forward)",
            MouseInputNames.WheelUp => "Wheel up",
            MouseInputNames.WheelDown => "Wheel down",
            "Escape" => "Esc",
            _ => input
        };
    }
}
