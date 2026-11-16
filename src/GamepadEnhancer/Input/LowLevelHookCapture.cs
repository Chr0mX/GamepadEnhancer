using System;

namespace GamepadEnhancer
{
    /// <summary>Capture backend that wraps the existing <see cref="GlobalInputHook"/> (WH_MOUSE_LL / WH_KEYBOARD_LL).</summary>
    internal sealed class LowLevelHookCapture : IInputCaptureSource
    {
        private readonly GlobalInputHook hook = new();

        public LowLevelHookCapture(IInputEventReceiver receiver)
        {
            hook.LeftButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Left, down, IntPtr.Zero);
            hook.RightButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Right, down, IntPtr.Zero);
            hook.MiddleButtonStateChanged += down => receiver.MouseButtonChanged(Method, MouseButton.Middle, down, IntPtr.Zero);
            hook.WheelScrolled += () => receiver.WheelScrolled(Method);
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
