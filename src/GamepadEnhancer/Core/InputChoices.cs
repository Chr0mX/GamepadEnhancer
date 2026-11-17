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
            for (int i = 1; i <= 24; i++) keys.Add("F" + i.ToString(CultureInfo.InvariantCulture));
            foreach (string extra in KeyNames.ExtraNames())
                if (!keys.Contains(extra)) keys.Add(extra);   // Insert, Delete, Page Up/Down, Caps Lock, numpad, punctuation, ...
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
            "PageUp" => "Page Up",
            "PageDown" => "Page Down",
            "CapsLock" => "Caps Lock",
            "PrintScreen" => "Print Screen",
            "NumLock" => "Num Lock",
            "NumpadMultiply" => "Num *",
            "NumpadAdd" => "Num +",
            "NumpadSubtract" => "Num -",
            "NumpadDecimal" => "Num .",
            "NumpadDivide" => "Num /",
            "Minus" => "- (Minus)",
            "Equals" => "= (Equals)",
            "LeftBracket" => "[ (Left bracket)",
            "RightBracket" => "] (Right bracket)",
            "Backslash" => "\\ (Backslash)",
            "Semicolon" => "; (Semicolon)",
            "Quote" => "' (Quote)",
            "Comma" => ", (Comma)",
            "Period" => ". (Period)",
            "Slash" => "/ (Slash)",
            "Backtick" => "` (Backtick)",
            _ when input != null && input.StartsWith("Numpad", StringComparison.Ordinal) && input.Length == 7 => "Num " + input[6],
            _ => input
        };
    }

    /// <summary>What a "press a key" capture saw.</summary>
    internal enum CapturedKind { Key, MouseButton, Wheel }

    /// <summary>Rules for turning a captured press into a binding. Pure, so it can be unit tested.</summary>
    internal static class InputCapture
    {
        /// <summary>The binding for a pad action, or an error to show the user. The left-stick directions take keyboard keys only.</summary>
        public static bool TryAcceptForAction(string action, CapturedKind kind, string name, out string input, out string error)
        {
            input = null;
            error = null;
            string canonical = InputChoices.Canonical(name);
            InputKind expected = kind switch { CapturedKind.Key => InputKind.Keyboard, CapturedKind.MouseButton => InputKind.MouseButton, _ => InputKind.MouseWheel };
            if (canonical == InputChoices.None || InputChoices.KindOf(canonical) != expected)
            {
                error = kind == CapturedKind.Key ? $"That key ({name}) can't be used; pick it from the list instead." : "That input can't be used.";
                return false;
            }
            if (!InputChoices.IsAllowed(action, canonical)) { error = "A left stick direction takes keyboard keys only."; return false; }
            input = canonical;
            return true;
        }

        /// <summary>The target for a rear paddle: a keyboard key or a mouse button (a wheel notch cannot be held).</summary>
        public static bool TryAcceptForPaddle(CapturedKind kind, string name, out PaddleTargetKind target, out string input, out string error)
        {
            target = PaddleTargetKind.None;
            input = null;
            error = null;
            if (kind == CapturedKind.Wheel) { error = "A paddle can't press the mouse wheel."; return false; }
            PaddleTargetKind wanted = kind == CapturedKind.Key ? PaddleTargetKind.Key : PaddleTargetKind.MouseButton;
            string canonical = PaddleMappings.Canonical(wanted, name);
            if (canonical == null) { error = $"That input ({name}) can't be used for a paddle."; return false; }
            target = wanted;
            input = canonical;
            return true;
        }
    }
}
