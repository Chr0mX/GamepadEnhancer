using System;

namespace GamepadEnhancer
{
    /// <summary>Capture backend that wraps the existing <see cref="GlobalInputHook"/> (WH_MOUSE_LL / WH_KEYBOARD_LL).</summary>
    internal sealed class LowLevelHookCapture : IInputCaptureSource
    {
        private readonly GlobalInputHook hook = new();

        /// <summary>The block toggle / panic hotkey went down (on the hook thread, which is the UI thread).</summary>
        public event Action HotkeyPressed;

        public LowLevelHookCapture(IInputEventReceiver receiver, BlockPolicy policy = null)
        {
            hook.Policy = policy;
            hook.HotkeyPressed += () => HotkeyPressed?.Invoke();
            hook.XButtonStateChanged += (which, down) => receiver.MouseButtonChanged(Method, which == 1 ? MouseButton.X1 : MouseButton.X2, down, IntPtr.Zero);
            hook.LeftButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Left, down, IntPtr.Zero);
            hook.RightButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Right, down, IntPtr.Zero);
            hook.MiddleButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Middle, down, IntPtr.Zero);
            hook.WheelScrolled += direction => receiver.WheelScrolled(Method, direction);
            hook.KeyStateChanged += (vk, down) => receiver.KeyChanged(Method, vk, down, IntPtr.Zero);
        }

        public InputCaptureMethod Method => InputCaptureMethod.LowLevelHooks;

        public bool Start(out string error)
        {
            error = null;
            try
            {
                if (!hook.Start()) { error = "Low-level hooks are only available on Windows."; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Stop() => hook.Stop();

        public void Dispose() => hook.Dispose();
    }
}
