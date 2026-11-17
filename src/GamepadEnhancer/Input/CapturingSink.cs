using System;

namespace GamepadEnhancer
{
    /// <summary>One press seen while "press a key" was waiting.</summary>
    internal readonly struct CapturedInput
    {
        public CapturedInput(CapturedKind kind, string name) { Kind = kind; Name = name; }
        public CapturedKind Kind { get; }
        public string Name { get; }
    }

    /// <summary>
    /// Sits between the input manager and the controller engine. Normally it just passes everything on. While a capture is waiting
    /// it takes the next key, mouse button or wheel notch for itself (the engine never sees that press) and hands it to the
    /// listener. Releases and mouse movement are always passed on, so nothing the engine already saw pressed can stay stuck.
    /// </summary>
    internal sealed class CapturingSink : IInputSink
    {
        private readonly IInputSink inner;
        private volatile Action<CapturedInput> listener;

        public CapturingSink(IInputSink inner) { this.inner = inner ?? throw new ArgumentNullException(nameof(inner)); }

        public bool IsCapturing => listener != null;

        public void BeginCapture(Action<CapturedInput> onCaptured) => listener = onCaptured;
        public void EndCapture() => listener = null;

        private bool Take(CapturedKind kind, string name)
        {
            Action<CapturedInput> l = listener;
            if (l == null) return false;
            listener = null;                 // one press per capture
            try { l(new CapturedInput(kind, name)); } catch { }
            return true;
        }

        public void OnKey(string name, bool isDown)
        {
            if (isDown && Take(CapturedKind.Key, name)) return;
            inner.OnKey(name, isDown);
        }

        public void OnMouseButton(string button, bool isDown)
        {
            if (isDown && Take(CapturedKind.MouseButton, button)) return;
            inner.OnMouseButton(button, isDown);
        }

        public void OnWheel(int direction)
        {
            if (direction != 0 && Take(CapturedKind.Wheel, direction > 0 ? MouseInputNames.WheelUp : MouseInputNames.WheelDown)) return;
            inner.OnWheel(direction);
        }

        public void OnMouseMove(int dx, int dy) => inner.OnMouseMove(dx, dy);
    }
}
