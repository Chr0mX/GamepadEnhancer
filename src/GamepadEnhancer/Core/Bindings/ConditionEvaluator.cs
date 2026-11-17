using System.Collections.Generic;

namespace GamepadEnhancer
{
    /// <summary>One press (down) or release (up) of a physical input, with the time it happened.</summary>
    internal readonly struct InputEdge
    {
        public InputEdge(bool down, long timeMs) { Down = down; TimeMs = timeMs; }
        public bool Down { get; }
        public long TimeMs { get; }
    }

    internal readonly struct EvaluationResult
    {
        public EvaluationResult(bool running, bool latched, BindingState state) { Running = running; Latched = latched; State = state; }
        /// <summary>The binding is executing right now: its outputs must be held.</summary>
        public bool Running { get; }
        /// <summary>A toggled binding that is latched on (also Running).</summary>
        public bool Latched { get; }
        public BindingState State { get; }
    }

    /// <summary>
    /// The state machine of one binding. Pure: time and edges go in, "is it running" comes out. It tracks the physical input level
    /// (<see cref="Down"/>) separately from whether the binding is executing, so a toggled binding keeps running after the input is
    /// released and a run-once binding stops by itself after its pulse.
    ///
    /// Edges, not levels, drive Press / Release / Double / Tap / Hold, so a press shorter than one engine tick is still seen. Every
    /// run-once event fires exactly once: a held input never re-fires, and one press can never toggle twice.
    /// </summary>
    internal sealed class ConditionEvaluator
    {
        private bool down;
        private long pressedAtMs;
        private bool holdArmed;
        private bool waitingSecond;
        private long firstPressAtMs;
        private bool latched;
        private long pulseUntilMs = long.MinValue;

        /// <summary>The physical input is held right now.</summary>
        public bool Down => down;
        public bool Latched => latched;

        /// <summary>Starts from the input's current level without counting it as a press (a binding created while the key is held).</summary>
        public void SeedLevel(bool isDown) { down = isDown; pressedAtMs = 0; holdArmed = false; waitingSecond = false; }

        /// <summary>Forgets everything: no latch, no pending timing, no pulse. Used when the binding is stopped from outside.</summary>
        public void Reset(bool keepLevel = false)
        {
            if (!keepLevel) down = false;
            holdArmed = false;
            waitingSecond = false;
            latched = false;
            pulseUntilMs = long.MinValue;
        }

        public EvaluationResult Update(BindingDef def, long nowMs, IReadOnlyList<InputEdge> edges)
        {
            if (!def.Enabled)
            {
                Reset(keepLevel: true);
                return new EvaluationResult(false, false, BindingState.Disabled);
            }
            if (def.IsAnalog)
                return new EvaluationResult(true, false, BindingState.Running);   // analog bindings are always running while enabled

            int fires = 0;
            bool pressedThisUpdate = false;
            if (edges != null)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    InputEdge e = edges[i];
                    if (e.Down)
                    {
                        if (down) continue;   // a repeat of a press we already have
                        down = true;
                        pressedAtMs = e.TimeMs;
                        pressedThisUpdate = true;
                        switch (def.Condition)
                        {
                            case BindingCondition.Press:
                                fires++;
                                break;
                            case BindingCondition.Double:
                                if (waitingSecond && e.TimeMs - firstPressAtMs <= def.DoubleWindowMs) { fires++; waitingSecond = false; }
                                else { waitingSecond = true; firstPressAtMs = e.TimeMs; }
                                break;
                            case BindingCondition.Hold:
                                holdArmed = true;
                                break;
                        }
                    }
                    else
                    {
                        if (!down) continue;  // a release we never saw the press for
                        down = false;
                        switch (def.Condition)
                        {
                            case BindingCondition.Release:
                                fires++;
                                break;
                            case BindingCondition.Tap:
                                if (e.TimeMs - pressedAtMs <= def.TapMaxMs) fires++;
                                break;
                            case BindingCondition.Hold:
                                // released at or after the hold time without an update in between: it was a hold
                                if (holdArmed && e.TimeMs - pressedAtMs >= def.HoldMs) fires++;
                                holdArmed = false;
                                break;
                        }
                    }
                }
            }

            if (holdArmed && down && def.Condition == BindingCondition.Hold && nowMs - pressedAtMs >= def.HoldMs)
            {
                fires++;
                holdArmed = false;   // once per press
            }
            if (waitingSecond && nowMs - firstPressAtMs > def.DoubleWindowMs) waitingSecond = false;

            bool running;
            if (def.Condition == BindingCondition.Held)
                running = down || pressedThisUpdate;
            else if (def.Condition == BindingCondition.Unheld)
                running = !down;
            else if (def.EffectiveToggle)
            {
                for (int i = 0; i < fires; i++) latched = !latched;
                running = latched;
            }
            else
            {
                if (fires > 0) pulseUntilMs = nowMs + BindingDef.PulseMs;
                running = nowMs < pulseUntilMs;
            }

            BindingState state;
            if (running) state = def.EffectiveToggle ? BindingState.ToggledActive : BindingState.Running;
            else if (holdArmed && down && def.Condition == BindingCondition.Hold) state = BindingState.WaitingHold;
            else if (waitingSecond && def.Condition == BindingCondition.Double) state = BindingState.WaitingDouble;
            else state = BindingState.Waiting;
            return new EvaluationResult(running, def.EffectiveToggle && latched, state);
        }
    }
}
