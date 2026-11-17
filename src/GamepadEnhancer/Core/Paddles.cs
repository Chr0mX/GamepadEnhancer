using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>What a rear paddle does when pressed.</summary>
    public enum PaddleTargetKind { None, PadButton, Key, MouseButton }

    /// <summary>One paddle's mapping: whether it is on, and what it presses (a virtual pad button, a keyboard key or a mouse button).</summary>
    public sealed class PaddleSetting
    {
        public bool Enabled { get; init; }
        public PaddleTargetKind Kind { get; init; }
        public string Input { get; init; } = "None";

        public static PaddleSetting Off { get; } = new();
        public bool HasTarget => Kind != PaddleTargetKind.None && !string.Equals(Input, "None", StringComparison.OrdinalIgnoreCase);
        public bool IsActive => Enabled && HasTarget;
    }

    /// <summary>The four rear paddle slots of a profile. Slot order is fixed (it is the order in files and the UI).</summary>
    internal static class PaddleSlots
    {
        public const int Count = 4;
        /// <summary>The canonical ids of the four slots (PADDLE_LEFT_UPPER, ...); these are also the diagram slot ids and the profile keys.</summary>
        public static readonly string[] Names = Inputs.Paddles;
        /// <summary>Neutral titles; a controller layout may label its paddles differently (see <see cref="ControllerLayoutDef.Label"/>).</summary>
        public static readonly string[] Titles = { Inputs.NeutralLabel(Inputs.PaddleLeftUpper), Inputs.NeutralLabel(Inputs.PaddleLeftLower),
                                                   Inputs.NeutralLabel(Inputs.PaddleRightUpper), Inputs.NeutralLabel(Inputs.PaddleRightLower) };

        public static string Id(int slot) => Names[slot];

        /// <summary>The slot for a canonical id (case-insensitive); older spellings ("LeftUpper", "Paddle:LeftUpper") are accepted too.</summary>
        public static bool TryParseId(string id, out int slot)
        {
            slot = -1;
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (id.StartsWith("Paddle:", StringComparison.Ordinal)) id = id.Substring(7);
            string canonical = LegacyIds.ToCanonical(id) ?? id;
            slot = Array.FindIndex(Names, n => string.Equals(n, canonical, StringComparison.OrdinalIgnoreCase));
            return slot >= 0;
        }

        /// <summary>Which hardware paddle index (0..3 = Paddle1..4) feeds each slot; anything invalid falls back to 0,1,2,3.</summary>
        public static int[] NormalizeSourceMap(int[] map)
        {
            if (map == null || map.Length != Count || map.Any(v => v < 0 || v >= Count) || map.Distinct().Count() != Count)
                return new[] { 0, 1, 2, 3 };
            return (int[])map.Clone();
        }
    }

    /// <summary>One paddle entry as stored in a profile file.</summary>
    public sealed class PaddleEntry
    {
        public bool Enabled { get; set; }
        /// <summary>"PadButton", "Key", "MouseButton" or "None".</summary>
        public string Kind { get; set; } = "None";
        public string Input { get; set; } = "None";
    }

    /// <summary>Reading, validating and building a profile's paddle mappings. Pure.</summary>
    internal static class PaddleMappings
    {
        /// <summary>Pad buttons a paddle can press: the 18 real buttons (not the left-stick direction keys).</summary>
        public static readonly string[] PadButtonTargets =
            ControllerEngine.BindingActions.Take(Array.IndexOf(ControllerEngine.BindingActions, ControllerEngine.ActionTouchpad) + 1).ToArray();

        public static PaddleTargetKind ParseKind(string text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "padbutton" => PaddleTargetKind.PadButton,
            "key" => PaddleTargetKind.Key,
            "mousebutton" => PaddleTargetKind.MouseButton,
            _ => PaddleTargetKind.None
        };

        public static string KindName(PaddleTargetKind kind) => kind switch
        {
            PaddleTargetKind.PadButton => "PadButton",
            PaddleTargetKind.Key => "Key",
            PaddleTargetKind.MouseButton => "MouseButton",
            _ => "None"
        };

        /// <summary>The input's canonical name for this kind, or null when it is not a valid target of that kind.</summary>
        public static string Canonical(PaddleTargetKind kind, string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            if (kind == PaddleTargetKind.PadButton) input = LegacyIds.ToCanonical(input) ?? input;   // older profiles stored "A / Cross" style ids
            string[] list = kind switch
            {
                PaddleTargetKind.PadButton => PadButtonTargets,
                PaddleTargetKind.Key => InputChoices.Keyboard,
                PaddleTargetKind.MouseButton => InputChoices.MouseButtons,
                _ => Array.Empty<string>()
            };
            return list.FirstOrDefault(n => string.Equals(n, input.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The four paddle settings of a profile. A missing <c>Paddles</c> block means all off. Invalid targets are cleared and two
        /// enabled paddles with the same target keep only the first (the rest are cleared); both are reported in <paramref name="warnings"/>.
        /// </summary>
        public static PaddleSetting[] Resolve(InputProfile profile, List<string> warnings = null)
        {
            var result = new PaddleSetting[PaddleSlots.Count];
            for (int i = 0; i < result.Length; i++) result[i] = PaddleSetting.Off;
            if (profile?.Paddles == null) return result;

            for (int i = 0; i < PaddleSlots.Count; i++)
            {
                PaddleEntry e = null;
                foreach (var pair in profile.Paddles)
                    if (TryGetSlot(pair.Key, out int keySlot) && keySlot == i) { e = pair.Value; break; }
                if (e == null) continue;

                PaddleTargetKind kind = ParseKind(e.Kind);
                string input = kind == PaddleTargetKind.None ? "None" : Canonical(kind, e.Input);
                if (kind != PaddleTargetKind.None && input == null)
                {
                    warnings?.Add($"{PaddleSlots.Titles[i]}: unknown {PaddleKindText(kind)} \"{e.Input}\" was cleared");
                    kind = PaddleTargetKind.None;
                    input = "None";
                }
                result[i] = new PaddleSetting { Enabled = e.Enabled, Kind = kind, Input = input };
            }

            for (int i = 0; i < result.Length; i++)
                for (int j = i + 1; j < result.Length; j++)
                    if (result[i].IsActive && result[j].IsActive && SameTarget(result[i], result[j]))
                    {
                        warnings?.Add($"{PaddleSlots.Titles[j]}: same target as {PaddleSlots.Titles[i]} ({result[j].Input}); cleared");
                        result[j] = new PaddleSetting { Enabled = false, Kind = PaddleTargetKind.None, Input = "None" };
                    }
            return result;
        }

        private static bool TryGetSlot(string key, out int slot) => PaddleSlots.TryParseId(key, out slot);

        private static string PaddleKindText(PaddleTargetKind k) => k switch
        {
            PaddleTargetKind.PadButton => "pad button",
            PaddleTargetKind.Key => "key",
            _ => "mouse button"
        };

        public static bool SameTarget(PaddleSetting a, PaddleSetting b) =>
            a != null && b != null && a.Kind == b.Kind && string.Equals(a.Input, b.Input, StringComparison.OrdinalIgnoreCase);

        /// <summary>The writable <c>Paddles</c> block for a profile.</summary>
        public static Dictionary<string, PaddleEntry> Build(IReadOnlyList<PaddleSetting> settings)
        {
            var d = new Dictionary<string, PaddleEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < PaddleSlots.Count; i++)
            {
                PaddleSetting s = settings != null && i < settings.Count && settings[i] != null ? settings[i] : PaddleSetting.Off;
                d[PaddleSlots.Names[i]] = new PaddleEntry { Enabled = s.Enabled, Kind = KindName(s.Kind), Input = s.HasTarget ? s.Input : "None" };
            }
            return d;
        }

        /// <summary>Index of another enabled paddle that already uses this target, or -1.</summary>
        public static int FindConflict(IReadOnlyList<PaddleSetting> settings, int slot, PaddleTargetKind kind, string input)
        {
            for (int i = 0; i < settings.Count && i < PaddleSlots.Count; i++)
                if (i != slot && settings[i] != null && settings[i].IsActive && settings[i].Kind == kind
                    && string.Equals(settings[i].Input, input, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>
        /// Changes one paddle. Refuses (and changes nothing) when the target is invalid or when another enabled paddle already
        /// uses it, so two paddles can never be given the same function by accident.
        /// </summary>
        public static bool TryAssign(PaddleSetting[] settings, int slot, bool enabled, PaddleTargetKind kind, string input, out string error)
        {
            error = null;
            if (settings == null || slot < 0 || slot >= PaddleSlots.Count || settings.Length < PaddleSlots.Count) { error = "No such paddle."; return false; }
            string canonical = "None";
            if (kind != PaddleTargetKind.None)
            {
                canonical = Canonical(kind, input);
                if (canonical == null) { error = "That is not a valid target for this paddle."; return false; }
            }
            if (enabled && kind != PaddleTargetKind.None)
            {
                int other = FindConflict(settings, slot, kind, canonical);
                if (other >= 0) { error = $"{canonical} is already used by the {PaddleSlots.Titles[other]}."; return false; }
            }
            settings[slot] = new PaddleSetting { Enabled = enabled, Kind = kind, Input = canonical };
            return true;
        }
    }

    // ── Hardware side ─────────────────────────────────────────────────────────

    /// <summary>One look at the physical controller: connection, which paddle bits are held (bit i = hardware paddle i), and who it is.</summary>
    internal readonly struct PaddleReading
    {
        public PaddleReading(bool connected, int mask, string deviceName, int vendorId, int productId)
        {
            Connected = connected; Mask = mask & 0xF; DeviceName = deviceName ?? ""; VendorId = vendorId; ProductId = productId;
        }
        public bool Connected { get; }
        public int Mask { get; }
        public string DeviceName { get; }
        public int VendorId { get; }
        public int ProductId { get; }
        public static PaddleReading None => new(false, 0, "", 0, 0);
    }

    /// <summary>A physical-controller reader. Implementations must be thread-safe and never throw.</summary>
    internal interface IPaddleSource
    {
        PaddleReading Read();
    }

    /// <summary>What can be pressed on the keyboard or mouse (the name is the one used in profiles).</summary>
    internal readonly struct InjectedInput
    {
        public InjectedInput(PaddleTargetKind kind, string name) { Kind = kind; Name = name; }
        public PaddleTargetKind Kind { get; }
        public string Name { get; }
    }

    internal interface IInputInjector
    {
        void Send(InjectedInput input, bool down);
    }

    /// <summary>
    /// Which hardware paddles exist. Windows exposes no "paddle count" query, so this is a list of controllers known to have four
    /// (Xbox Elite Series 1 / 2, which Windows.Gaming.Input reports as Paddle1-4) plus every paddle bit that has actually been
    /// seen pressed. Any other controller is reported as having none, instead of assuming it does.
    /// </summary>
    internal static class PaddleCaps
    {
        private static readonly (int Vid, int Pid)[] KnownFourPaddle =
        {
            (0x045E, 0x02E3),   // Xbox Elite Wireless Controller Series 1
            (0x045E, 0x0B00),   // Xbox Elite Wireless Controller Series 2 (USB)
            (0x045E, 0x0B05),   // Xbox Elite Wireless Controller Series 2 (Bluetooth)
            (0x045E, 0x0B22),   // Xbox Elite Wireless Controller Series 2 (Bluetooth, newer firmware)
        };

        public static int KnownPaddleCount(int vendorId, int productId) =>
            KnownFourPaddle.Any(k => k.Vid == vendorId && k.Pid == productId) ? 4 : 0;

        /// <summary>Bit i = hardware paddle i is available: known from the model, or seen pressed.</summary>
        public static int AvailableMask(PaddleReading reading, int seenMask)
        {
            if (!reading.Connected) return 0;
            int known = KnownPaddleCount(reading.VendorId, reading.ProductId) >= 4 ? 0xF : 0;
            return (known | seenMask) & 0xF;
        }

        /// <summary>True when the slot's hardware paddle is available (<paramref name="sourceIndex"/> maps slot to hardware paddle).</summary>
        public static bool SlotAvailable(int availableMask, int[] sourceIndex, int slot)
        {
            int[] map = PaddleSlots.NormalizeSourceMap(sourceIndex);
            return slot >= 0 && slot < PaddleSlots.Count && ((availableMask >> map[slot]) & 1) != 0;
        }
    }

    /// <summary>
    /// Turns paddle presses into outputs, once per engine tick. Presses are edge based (one press, one release: nothing repeats),
    /// every slot is independent so simultaneous paddles work, a paddle already held when the controller (re)connects is ignored
    /// until it is released, changing or disabling a paddle while it is held releases the old target, and a disconnect releases
    /// everything. Pad-button targets come back as a bit mask; keyboard / mouse targets are queued and sent by <see cref="Flush"/>
    /// (outside the engine lock). Keys it injects itself are reported by <see cref="IsInjected"/> so the engine never treats them
    /// as the user's own input.
    /// </summary>
    internal sealed class PaddleProcessor
    {
        private static readonly Dictionary<string, int> ActionBit = BuildActionBits();

        private readonly object gate = new();
        private readonly PaddleSetting[] held = new PaddleSetting[PaddleSlots.Count];
        private readonly bool[] needRepress = new bool[PaddleSlots.Count];
        private readonly Dictionary<string, int> refs = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> injected = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(InjectedInput Input, bool Down)> pending = new();
        private bool wasConnected;
        private int ignoreMask;
        private volatile int lastPadMask;
        private volatile int lastSlots;

        public IInputInjector Injector { get; set; }

        /// <summary>Pad-action bits (BindingActions index) held by paddles at the last update.</summary>
        public int LastPadMask => lastPadMask;
        /// <summary>Bit per slot: the paddle is currently held and mapped.</summary>
        public int LastHeldSlots => lastSlots;

        private static Dictionary<string, int> BuildActionBits()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ControllerEngine.BindingActions.Length; i++) d[ControllerEngine.BindingActions[i]] = i;
            return d;
        }

        /// <summary>True while this processor is holding the key / mouse button down itself.</summary>
        public bool IsInjected(string name) => name != null && injected.ContainsKey(name);

        public int Update(PaddleReading reading, PaddleSetting[] settings, int[] sourceIndex)
        {
            lock (gate)
            {
                if (!reading.Connected)
                {
                    ReleaseAllLocked();
                    wasConnected = false;
                    ignoreMask = 0;
                    return 0;
                }
                if (!wasConnected) { wasConnected = true; ignoreMask = reading.Mask; }
                ignoreMask &= reading.Mask;                 // a paddle held at connect time counts again once released
                int physical = reading.Mask & ~ignoreMask;
                int[] map = PaddleSlots.NormalizeSourceMap(sourceIndex);

                int padMask = 0, slots = 0;
                for (int i = 0; i < PaddleSlots.Count; i++)
                {
                    PaddleSetting st = settings != null && i < settings.Length ? settings[i] : null;
                    bool phys = ((physical >> map[i]) & 1) != 0;
                    if (!phys) needRepress[i] = false;

                    if (held[i] != null)
                    {
                        bool same = st != null && st.IsActive && PaddleMappings.SameTarget(held[i], st);
                        if (!phys || !same)
                        {
                            Release(held[i]);
                            held[i] = null;
                            if (phys) needRepress[i] = true;   // mapping changed mid-hold: needs a fresh press
                        }
                    }
                    if (held[i] == null && phys && !needRepress[i] && st != null && st.IsActive)
                    {
                        held[i] = st;
                        Press(st);
                    }
                    if (held[i] != null)
                    {
                        slots |= 1 << i;
                        if (held[i].Kind == PaddleTargetKind.PadButton && ActionBit.TryGetValue(held[i].Input, out int bit)) padMask |= 1 << bit;
                    }
                }
                lastPadMask = padMask;
                lastSlots = slots;
                return padMask;
            }
        }

        /// <summary>Releases everything held (pad bits cleared, queued key / mouse releases). Call <see cref="Flush"/> afterwards.</summary>
        public void ReleaseAll()
        {
            lock (gate) ReleaseAllLocked();
        }

        private void ReleaseAllLocked()
        {
            for (int i = 0; i < held.Length; i++)
            {
                if (held[i] != null) Release(held[i]);
                held[i] = null;
                needRepress[i] = false;
            }
            lastPadMask = 0;
            lastSlots = 0;
        }

        private static string RefKey(PaddleSetting s) => (s.Kind == PaddleTargetKind.Key ? "k:" : "m:") + s.Input;

        private void Press(PaddleSetting s)
        {
            if (s.Kind == PaddleTargetKind.PadButton || s.Kind == PaddleTargetKind.None) return;
            string key = RefKey(s);
            refs.TryGetValue(key, out int n);
            refs[key] = n + 1;
            if (n == 0)
            {
                injected[s.Input] = 1;                 // before the event exists, so its echo is never mistaken for the user
                pending.Add((new InjectedInput(s.Kind, s.Input), true));
            }
        }

        private void Release(PaddleSetting s)
        {
            if (s.Kind == PaddleTargetKind.PadButton || s.Kind == PaddleTargetKind.None) return;
            string key = RefKey(s);
            if (!refs.TryGetValue(key, out int n)) return;
            if (n <= 1)
            {
                refs.Remove(key);
                pending.Add((new InjectedInput(s.Kind, s.Input), false));
            }
            else refs[key] = n - 1;
        }

        /// <summary>Sends the queued key / mouse presses and releases. Call outside the engine lock.</summary>
        public void Flush()
        {
            (InjectedInput Input, bool Down)[] batch;
            lock (gate)
            {
                if (pending.Count == 0) return;
                batch = pending.ToArray();
                pending.Clear();
            }
            foreach (var item in batch)
            {
                try { Injector?.Send(item.Input, item.Down); } catch { /* an injection failure must not stop the engine */ }
                if (!item.Down) injected.TryRemove(item.Input.Name, out _);
            }
        }
    }
}
