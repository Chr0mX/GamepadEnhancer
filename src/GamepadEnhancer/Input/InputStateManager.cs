using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>
    /// Unified input-state manager. Exactly one capture backend (low-level hooks or Raw Input) is active; both report
    /// into this class, which tracks which keys/buttons are held (and on which device), drops events from any
    /// inactive backend, and forwards normalized events to the controller engine (<see cref="IInputSink"/>).
    /// Switching methods stops the old backend first, then releases every held key/button so nothing can stay stuck.
    /// </summary>
    internal sealed class InputStateManager : IInputEventReceiver, IDisposable
    {
        private readonly object transition = new();   // serializes SetMethod / Dispose
        private readonly object sync = new();         // protects held state and the active method
        private readonly IInputSink sink;
        private readonly Func<InputCaptureMethod, IInputEventReceiver, IInputCaptureSource> factory;
        private readonly Dictionary<string, IntPtr> heldKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<MouseButton, IntPtr> heldButtons = new();
        private IInputCaptureSource activeSource;
        private InputCaptureMethod? acceptedMethod;
        private bool disposed;

        public InputStateManager(IInputSink sink, Func<InputCaptureMethod, IInputEventReceiver, IInputCaptureSource> factory)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>The method whose events are currently accepted, or null if none is running.</summary>
        public InputCaptureMethod? ActiveMethod { get { lock (sync) return acceptedMethod; } }

        /// <summary>Raised (outside the lock, from the capturing thread) when the active backend fails. Switch methods to recover.</summary>
        public event Action<InputCaptureMethod, string> Faulted;

        /// <summary>Number of keys + mouse buttons currently tracked as held (for diagnostics and tests).</summary>
        public int HeldCount { get { lock (sync) return heldKeys.Count + heldButtons.Count; } }

        // ── Switching ────────────────────────────────────────────────────────────

        /// <summary>
        /// Makes <paramref name="method"/> the only active capture method. Call from the UI thread (the low-level hooks
        /// need a message loop). On failure the previous method is restored and false is returned.
        /// </summary>
        public bool SetMethod(InputCaptureMethod method, out string error)
        {
            lock (transition)
            {
                error = null;
                if (disposed) { error = "Input manager is disposed."; return false; }

                InputCaptureMethod? previous;
                lock (sync)
                {
                    if (acceptedMethod == method && activeSource != null) return true;
                    previous = acceptedMethod;
                }

                StopActive();   // old backend stopped; its late events are already ignored; held state released

                IInputCaptureSource next = factory(method, this);
                lock (sync) acceptedMethod = method;   // accept events as soon as the new backend starts
                bool started;
                try { started = next.Start(out error); }
                catch (Exception ex) { started = false; error = ex.Message; }

                if (!started)
                {
                    lock (sync) acceptedMethod = null;
                    SafeDispose(next);
                    ReleaseAll();
                    if (previous.HasValue && previous.Value != method) TryRestore(previous.Value);
                    return false;
                }

                lock (sync) activeSource = next;
                return true;
            }
        }

        private void TryRestore(InputCaptureMethod method)
        {
            IInputCaptureSource src = factory(method, this);
            lock (sync) acceptedMethod = method;
            bool ok;
            try { ok = src.Start(out _); } catch { ok = false; }
            if (ok) { lock (sync) activeSource = src; }
            else { lock (sync) acceptedMethod = null; SafeDispose(src); }
        }

        private void StopActive()
        {
            IInputCaptureSource old;
            lock (sync)
            {
                old = activeSource;
                activeSource = null;
                acceptedMethod = null;   // from now on every event is dropped
            }
            if (old != null)
            {
                try { old.Stop(); } catch { }
                SafeDispose(old);
            }
            ReleaseAll();
        }

        private static void SafeDispose(IDisposable d) { try { d?.Dispose(); } catch { } }

        public void Dispose()
        {
            lock (transition)
            {
                if (disposed) return;
                disposed = true;
                StopActive();
            }
        }

        // ── Releasing held state ─────────────────────────────────────────────────

        /// <summary>Sends an "up" to the engine for everything still held, then clears the state.</summary>
        private void ReleaseAll()
        {
            lock (sync)
            {
                foreach (string key in heldKeys.Keys.ToList()) sink.OnKey(key, false);
                foreach (MouseButton b in heldButtons.Keys.ToList()) sink.OnMouseButton(InputCaptureMethods.ButtonName(b), false);
                heldKeys.Clear();
                heldButtons.Clear();
            }
        }

        // ── Events from the backends ─────────────────────────────────────────────

        public void KeyChanged(InputCaptureMethod source, int vk, bool isDown, IntPtr device)
        {
            lock (sync)
            {
                if (acceptedMethod != source) return;   // inactive backend: ignore (no duplicates across methods)
                string name = KeyNames.FromVirtualKey(vk);
                if (isDown)
                {
                    heldKeys[name] = device;
                    sink.OnKey(name, true);              // auto-repeat downs are forwarded, as with the original hooks
                }
                else if (heldKeys.Remove(name))
                {
                    sink.OnKey(name, false);
                }                                         // an "up" for a key we don't hold is stale: dropped
            }
        }

        public void MouseButtonChanged(InputCaptureMethod source, MouseButton button, bool isDown, IntPtr device)
        {
            lock (sync)
            {
                if (acceptedMethod != source) return;
                if (isDown)
                {
                    if (heldButtons.ContainsKey(button)) return;   // duplicate down
                    heldButtons[button] = device;
                    sink.OnMouseButton(InputCaptureMethods.ButtonName(button), true);
                }
                else if (heldButtons.Remove(button))
                {
                    sink.OnMouseButton(InputCaptureMethods.ButtonName(button), false);
                }
            }
        }

        public void WheelScrolled(InputCaptureMethod source, int direction)
        {
            lock (sync)
            {
                if (acceptedMethod != source) return;
                sink.OnWheel(direction);
            }
        }

        /// <summary>Movement comes from its own Raw Input source, independent of the active key/button method, and needs no held state.</summary>
        public void MouseMoved(InputCaptureMethod source, int dx, int dy)
        {
            if (disposed) return;
            sink.OnMouseMove(dx, dy);
        }

        public void DeviceRemoved(InputCaptureMethod source, IntPtr device)
        {
            if (device == IntPtr.Zero) return;
            lock (sync)
            {
                if (acceptedMethod != source) return;
                foreach (string key in heldKeys.Where(p => p.Value == device).Select(p => p.Key).ToList())
                {
                    heldKeys.Remove(key);
                    sink.OnKey(key, false);
                }
                foreach (MouseButton b in heldButtons.Where(p => p.Value == device).Select(p => p.Key).ToList())
                {
                    heldButtons.Remove(b);
                    sink.OnMouseButton(InputCaptureMethods.ButtonName(b), false);
                }
            }
        }

        public void CaptureFaulted(InputCaptureMethod source, string message)
        {
            lock (sync)
            {
                if (acceptedMethod != source) return;
                ReleaseAllLocked();
            }
            try { Faulted?.Invoke(source, message); } catch { }
        }

        private void ReleaseAllLocked() => ReleaseAll();   // sync is re-entrant (same thread)
    }
}
