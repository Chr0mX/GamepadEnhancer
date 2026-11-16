using System;

namespace GamepadEnhancer
{
    /// <summary>A capture backend (low-level hooks or Raw Input). It reports raw events to an <see cref="IInputEventReceiver"/>.</summary>
    internal interface IInputCaptureSource : IDisposable
    {
        InputCaptureMethod Method { get; }

        /// <summary>Starts capturing. Returns false (with a message) if this method is unavailable.</summary>
        bool Start(out string error);

        /// <summary>Stops capturing. No events are delivered after this returns. Safe to call more than once.</summary>
        void Stop();
    }

    /// <summary>Receives events from capture backends; implemented by <see cref="InputStateManager"/>. Thread-safe.</summary>
    internal interface IInputEventReceiver
    {
        /// <param name="vk">Windows virtual-key code.</param>
        /// <param name="device">Source device handle (IntPtr.Zero when unknown or synthetic).</param>
        void KeyChanged(InputCaptureMethod source, int vk, bool isDown, System.IntPtr device);
        void MouseButtonChanged(InputCaptureMethod source, MouseButton button, bool isDown, System.IntPtr device);
        /// <param name="direction">Positive = up, negative = down, 0 = unknown.</param>
        void WheelScrolled(InputCaptureMethod source, int direction);
        /// <summary>Relative mouse movement (counts). Only the movement-only Raw Input source reports this.</summary>
        void MouseMoved(InputCaptureMethod source, int dx, int dy);
        /// <summary>A device was unplugged: anything it was holding is released.</summary>
        void DeviceRemoved(InputCaptureMethod source, System.IntPtr device);
        /// <summary>The backend failed while running.</summary>
        void CaptureFaulted(InputCaptureMethod source, string message);
    }
}
