using System;

namespace GamepadEnhancer
{
    /// <summary>How physical mouse/keyboard input is captured. Exactly one method is active at a time.</summary>
    public enum InputCaptureMethod
    {
        /// <summary>WH_MOUSE_LL / WH_KEYBOARD_LL hooks (<see cref="GlobalInputHook"/>), the original method.</summary>
        LowLevelHooks,
        /// <summary>Windows Raw Input (<c>RegisterRawInputDevices</c> + <c>WM_INPUT</c> + <c>RIDEV_INPUTSINK</c>).</summary>
        RawInput
    }

    public enum MouseButton { Left, Right, Middle }

    public static class InputCaptureMethods
    {
        public const string LowLevelHooksLabel = "Low-Level Hooks";
        public const string RawInputLabel = "Raw Input";

        public static readonly string[] Labels = { LowLevelHooksLabel, RawInputLabel };

        public static string ToLabel(InputCaptureMethod m) => m == InputCaptureMethod.RawInput ? RawInputLabel : LowLevelHooksLabel;

        public static InputCaptureMethod FromLabel(string label) => label == RawInputLabel ? InputCaptureMethod.RawInput : InputCaptureMethod.LowLevelHooks;

        /// <summary>Value stored in settings.json.</summary>
        public static string ToStored(InputCaptureMethod m) => m == InputCaptureMethod.RawInput ? "RawInput" : "LowLevelHooks";

        public static InputCaptureMethod FromStored(string stored)
            => string.Equals(stored, "RawInput", StringComparison.OrdinalIgnoreCase) ? InputCaptureMethod.RawInput : InputCaptureMethod.LowLevelHooks;

        public static string ButtonName(MouseButton b) => b switch
        {
            MouseButton.Right => "Mouse Right",
            MouseButton.Middle => "Mouse Middle",
            _ => "Mouse Left"
        };
    }
}
