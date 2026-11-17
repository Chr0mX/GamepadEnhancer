using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>What the UI shows for one binding. Built by the runtime from the real execution state, never from the settings.</summary>
    internal readonly struct BindingStatus
    {
        public BindingStatus(string id, BindingState state, bool running, bool latched, bool physicalDown)
        { Id = id; State = state; Running = running; Latched = latched; PhysicalDown = physicalDown; }
        public string Id { get; }
        public BindingState State { get; }
        /// <summary>Executing right now (outputs held). Enabled-but-idle bindings are not running.</summary>
        public bool Running { get; }
        public bool Latched { get; }
        /// <summary>The physical input is held. Separate from Running: a toggled binding runs with the input released.</summary>
        public bool PhysicalDown { get; }
    }

    /// <summary>Key and mouse presses a binding wants sent; applied by <see cref="BindingRuntime.Flush"/> outside every lock.</summary>
    internal readonly struct PendingInjection
    {
        public PendingInjection(BindingOutputKind kind, string name, bool down) { Kind = kind; Name = name; Down = down; }
        public BindingOutputKind Kind { get; }
        public string Name { get; }
        public bool Down { get; }
    }

    /// <summary>
    /// Runs the binding list. The input threads only enqueue timestamped edges (<see cref="OnEdge"/>); the engine tick calls
    /// <see cref="Update"/>, which feeds every binding's <see cref="ConditionEvaluator"/>, claims and releases outputs through
    /// <see cref="OutputOwnership"/>, and publishes a status snapshot the UI reads without any lock. Nothing here sleeps or starts a
    /// timer: time is whatever the caller passes in, so tests drive it with a fake clock.
    /// </summary>
    internal sealed class BindingRuntime
    {
        private sealed class Entry
        {
            public BindingDef Def;
            public readonly ConditionEvaluator Eval = new();
            public bool Running;
        }

        private readonly object gate = new();
        private readonly List<(string Input, InputEdge Edge)> queue = new();
        private readonly Dictionary<string, bool> levels = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entry> entries = new();
        private readonly OutputOwnership ownership = new();
        private readonly List<PendingInjection> injections = new();
        private readonly Dictionary<string, long> injectedUntilTicks = new(StringComparer.OrdinalIgnoreCase);
        private volatile BindingStatus[] snapshot = Array.Empty<BindingStatus>();
        private volatile HashSet<string> suppressed = new(StringComparer.OrdinalIgnoreCase);
        private int padMask;
        private byte leftTrigger, rightTrigger;

        /// <summary>How long after a release an injected key still counts as ours (its echo from the hook arrives a little later).</summary>
        private const long InjectedGraceTicksMs = 300;
        private const int MaxQueuedEdges = 4096;

        /// <summary>Bit mask over <see cref="ControllerEngine.BindingActions"/> of the pad buttons the running bindings hold.</summary>
        public int PadMask { get { lock (gate) return padMask; } }

        /// <summary>The level of the pad triggers held by bindings, 0..255 (0 = none).</summary>
        public byte TriggerLevel(bool left) { lock (gate) return left ? leftTrigger : rightTrigger; }

        public BindingStatus[] Snapshot() => snapshot;

        /// <summary>True when an enabled, non-analog binding uses this input: the automatic mapping of that input must stay silent.</summary>
        public bool Suppresses(string input) => input != null && suppressed.Contains(input);

        /// <summary>True for a key / mouse button a binding is (or has just been) pressing itself, so the engine does not treat it as user input.</summary>
        public bool IsInjected(string name)
        {
            if (name == null) return false;
            lock (gate)
            {
                if (injectedUntilTicks.TryGetValue(name, out long until)) return until == long.MaxValue || Environment.TickCount64 <= until;
                return false;
            }
        }

        // ── Definitions ─────────────────────────────────────────────────────────

        /// <summary>
        /// Replaces the binding list. A binding whose behaviour did not change keeps its state (a latched binding stays latched when the
        /// user edits an unrelated one); a changed or removed binding releases what it held first, then starts clean.
        /// </summary>
        public void SetBindings(IReadOnlyList<BindingDef> defs)
        {
            lock (gate)
            {
                var next = new List<Entry>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (BindingDef d in defs ?? Array.Empty<BindingDef>())
                {
                    if (d == null || string.IsNullOrEmpty(d.Id) || !seen.Add(d.Id)) continue;
                    Entry old = entries.FirstOrDefault(e => e.Def.Id == d.Id);
                    if (old != null && old.Def.SameBehaviour(d)) { old.Def = d; next.Add(old); continue; }
                    if (old != null) StopLocked(old);
                    var fresh = new Entry { Def = d };
                    fresh.Eval.SeedLevel(levels.TryGetValue(d.Input ?? "", out bool lv) && lv);
                    next.Add(fresh);
                }
                foreach (Entry gone in entries.Where(e => !seen.Contains(e.Def.Id)).ToList()) StopLocked(gone);
                entries.Clear();
                entries.AddRange(next);
                suppressed = new HashSet<string>(entries.Where(e => e.Def.Enabled && !e.Def.IsAnalog && e.Def.Input != null).Select(e => e.Def.Input), StringComparer.OrdinalIgnoreCase);
                RecomputeLocked();
                PublishLocked();
            }
        }

        // ── Input ───────────────────────────────────────────────────────────────

        /// <summary>Called from the input threads: records a press or release with its time. Cheap and lock-light.</summary>
        public void OnEdge(string input, bool down, long timeMs)
        {
            if (string.IsNullOrEmpty(input)) return;
            lock (gate)
            {
                if (queue.Count >= MaxQueuedEdges) queue.RemoveRange(0, queue.Count / 2);   // Update is not running (no pad connected): do not grow forever
                queue.Add((input, new InputEdge(down, timeMs)));
            }
        }

        /// <summary>One engine tick: applies the queued edges and the clock to every binding and updates outputs and the snapshot.</summary>
        public void Update(long nowMs)
        {
            lock (gate)
            {
                // physical levels, ignoring a repeat of the same state so the evaluators only see real transitions
                var perInput = new Dictionary<string, List<InputEdge>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (input, edge) in queue)
                {
                    levels.TryGetValue(input, out bool was);
                    if (was == edge.Down) continue;
                    levels[input] = edge.Down;
                    if (!perInput.TryGetValue(input, out var list)) perInput[input] = list = new List<InputEdge>();
                    list.Add(edge);
                }
                queue.Clear();

                foreach (Entry e in entries)
                {
                    List<InputEdge> edges = null;
                    if (e.Def.Input != null) perInput.TryGetValue(e.Def.Input, out edges);
                    EvaluationResult r = e.Eval.Update(e.Def, nowMs, edges);
                    if (r.Running && !e.Running) StartLocked(e);
                    else if (!r.Running && e.Running) StopOutputsLocked(e);
                    e.Running = r.Running;
                    statuses[e] = new BindingStatus(e.Def.Id, r.State, r.Running, r.Latched, e.Eval.Down);
                }
                RecomputeLocked();
                PublishLocked();
            }
        }

        private readonly Dictionary<object, BindingStatus> statuses = new();

        private void PublishLocked()
        {
            var list = new BindingStatus[entries.Count];
            for (int i = 0; i < list.Length; i++)
            {
                Entry e = entries[i];
                list[i] = statuses.TryGetValue(e, out BindingStatus s) ? s
                    : new BindingStatus(e.Def.Id, e.Def.Enabled ? BindingState.Waiting : BindingState.Disabled, false, false, e.Eval.Down);
            }
            snapshot = list;
        }

        // ── Outputs ─────────────────────────────────────────────────────────────

        private void StartLocked(Entry e)
        {
            if (e.Def.IsAnalog) return;   // analog bindings report "running"; the analog path itself (mouse look) does the work
            foreach (BindingOutput o in e.Def.Outputs)
            {
                if (ownership.Claim(e.Def.Id, o.Key, o.IsTrigger ? o.Magnitude : 100)) OutputChangedLocked(o.Kind, o.Name, o.Key);
            }
        }

        private void StopOutputsLocked(Entry e)
        {
            foreach (string key in ownership.ReleaseAll(e.Def.Id)) OutputChangedKeyLocked(key);
        }

        private void StopLocked(Entry e)
        {
            StopOutputsLocked(e);
            e.Eval.Reset();
            e.Running = false;
            statuses.Remove(e);
        }

        private void OutputChangedKeyLocked(string key)
        {
            int colon = key.IndexOf(':');
            if (colon < 0) return;
            if (!Enum.TryParse(key.Substring(0, colon), out BindingOutputKind kind)) return;
            OutputChangedLocked(kind, key.Substring(colon + 1), key);
        }

        /// <summary>A key or mouse button went down or up: queue the injection and remember the name as ours.</summary>
        private void OutputChangedLocked(BindingOutputKind kind, string name, string key)
        {
            if (kind == BindingOutputKind.PadButton) return;   // pad buttons are read by the engine through PadMask / TriggerLevel
            bool down = ownership.IsHeld(key);
            injections.Add(new PendingInjection(kind, name, down));
            injectedUntilTicks[name] = down ? long.MaxValue : Environment.TickCount64 + InjectedGraceTicksMs;
        }

        private void RecomputeLocked()
        {
            int mask = 0;
            byte lt = 0, rt = 0;
            foreach (string key in ownership.HeldOutputs)
            {
                if (!key.StartsWith("PadButton:", StringComparison.Ordinal)) continue;
                string name = key.Substring("PadButton:".Length);
                int bit = Array.FindIndex(ControllerEngine.BindingActions, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                if (bit >= 0) mask |= 1 << bit;
                byte level = (byte)Math.Clamp((ownership.Level(key) * 255 + 50) / 100, 1, 255);
                if (string.Equals(name, Inputs.Lt, StringComparison.OrdinalIgnoreCase)) lt = level;
                else if (string.Equals(name, Inputs.Rt, StringComparison.OrdinalIgnoreCase)) rt = level;
            }
            padMask = mask;
            leftTrigger = lt;
            rightTrigger = rt;
        }

        /// <summary>Sends the queued key / mouse presses and releases. Call it outside the engine lock.</summary>
        public void Flush(IInputInjector injector)
        {
            PendingInjection[] batch;
            lock (gate)
            {
                if (injections.Count == 0) { PurgeInjectedLocked(); return; }
                batch = injections.ToArray();
                injections.Clear();
                PurgeInjectedLocked();
            }
            foreach (PendingInjection p in batch)
            {
                try
                {
                    PaddleTargetKind kind = p.Kind == BindingOutputKind.Key ? PaddleTargetKind.Key : PaddleTargetKind.MouseButton;
                    injector?.Send(new InjectedInput(kind, p.Name), p.Down);
                }
                catch { /* an injection failure must not stop the engine */ }
            }
        }

        private void PurgeInjectedLocked()
        {
            if (injectedUntilTicks.Count == 0) return;
            long now = Environment.TickCount64;
            foreach (string name in injectedUntilTicks.Where(p => p.Value != long.MaxValue && p.Value < now).Select(p => p.Key).ToList())
                injectedUntilTicks.Remove(name);
        }

        /// <summary>Test hook: the injections not yet flushed.</summary>
        internal PendingInjection[] PendingForTests() { lock (gate) return injections.ToArray(); }

        // ── Stop everything ─────────────────────────────────────────────────────

        /// <summary>
        /// Disconnect, device loss, shutdown: every output is released, every binding is reset (no latch survives), queued edges and
        /// physical levels are forgotten. The binding list itself stays. Call <see cref="Flush"/> afterwards to send the releases.
        /// </summary>
        public void ReleaseAll()
        {
            lock (gate)
            {
                queue.Clear();
                levels.Clear();
                foreach (string key in ownership.ReleaseEverything()) OutputChangedKeyAfterClearLocked(key);
                foreach (Entry e in entries) { e.Eval.Reset(); e.Running = false; }
                statuses.Clear();
                RecomputeLocked();
                PublishLocked();
            }
        }

        /// <summary>The ownership map is already empty here, so the release is queued directly.</summary>
        private void OutputChangedKeyAfterClearLocked(string key)
        {
            int colon = key.IndexOf(':');
            if (colon < 0 || !Enum.TryParse(key.Substring(0, colon), out BindingOutputKind kind) || kind == BindingOutputKind.PadButton) return;
            string name = key.Substring(colon + 1);
            injections.Add(new PendingInjection(kind, name, false));
            injectedUntilTicks[name] = Environment.TickCount64 + InjectedGraceTicksMs;
        }
    }
}
