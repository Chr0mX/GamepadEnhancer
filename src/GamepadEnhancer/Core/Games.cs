using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GamepadEnhancer
{
    /// <summary>A canonical game action (JUMP, RELOAD, TACTICAL, ...): a stable id plus the game's own name for it.</summary>
    internal sealed class GameActionDef
    {
        /// <summary>Upper-case id, unique within the game. Profiles reference actions by this id, never by a controller button.</summary>
        public string Id { get; set; } = "";
        /// <summary>The game's wording ("Interact / Pickup").</summary>
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
    }

    /// <summary>Where a game puts an action on its own default controller layout.</summary>
    internal sealed class NativeBinding
    {
        /// <summary>A single canonical physical input (FACE_SOUTH, LB, ...).</summary>
        public string Input { get; set; }
        /// <summary>Several inputs held together (a combo such as LB + RB); used instead of <see cref="Input"/>.</summary>
        public List<string> Inputs { get; set; } = new();
        /// <summary>"tap" (default), "hold", "toggle" ... informational: the virtual pad cannot tell them apart, the game decides by duration.</summary>
        public string Gesture { get; set; } = "tap";

        [JsonIgnore] public bool IsCombo => Inputs.Count > 1;
        [JsonIgnore] public string Primary => Inputs.Count > 0 ? Inputs[0] : null;
        [JsonIgnore] public bool IsHold => string.Equals(Gesture, "hold", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One supported game, loaded from an embedded <c>Defaults/Games/&lt;id&gt;.json</c> (a new game is one new file). It holds the
    /// game's <b>action catalog</b> (canonical ids with the game's names) and its <b>native controller layouts</b>: for each preset,
    /// which physical input triggers each action. Nothing here names a controller brand: labels such as "Cross" or "A" are resolved
    /// from the controller layout at display time. <see cref="ProfileResource"/> optionally names an embedded profile with this app's
    /// default PC-input bindings for the game.
    /// </summary>
    internal sealed class GameDefinition
    {
        private IReadOnlyDictionary<string, string> native;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Where the native layout comes from (a verifiable description, never a guess).</summary>
        public string Source { get; set; } = "";
        /// <summary>False while the native layout has not been transcribed from a real source; such a game exposes no actions or labels.</summary>
        public bool Verified { get; set; }
        public string Layout { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProfileResource { get; set; }
        /// <summary>The controller layout this config is written for (a layout id); null = works with any controller.</summary>
        public string Controller { get; set; }
        public List<GameActionDef> Actions { get; set; } = new();
        /// <summary>Which entry of <see cref="NativeLayouts"/> is the game's default.</summary>
        public string NativePreset { get; set; } = "default";
        /// <summary>Preset name, then action id to where the game puts it.</summary>
        public Dictionary<string, Dictionary<string, NativeBinding>> NativeLayouts { get; set; } = new();

        /// <summary>Physical input id to the label of what the game does on it (derived, never hand-written).</summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, string> Native => native ??= BuildNative();

        /// <summary>The name shown in the config list: the name, plus the controller layout's own short name when the config targets one.</summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Controller) ? Name : $"{Name} ({ControllerLayouts.Get(Controller).Name})";

        public GameActionDef Action(string actionId) =>
            Actions.FirstOrDefault(a => string.Equals(a.Id, ActionIds.Normalize(actionId), StringComparison.OrdinalIgnoreCase));

        public string ActionName(string actionId) => Action(actionId)?.Name;

        private Dictionary<string, NativeBinding> DefaultPreset() =>
            NativeLayouts.TryGetValue(NativePreset ?? "default", out var preset) ? preset
            : NativeLayouts.Values.FirstOrDefault() ?? new Dictionary<string, NativeBinding>();

        public NativeBinding NativeBindingFor(string actionId) =>
            DefaultPreset().TryGetValue(ActionIds.Normalize(actionId), out NativeBinding b) ? b : null;

        /// <summary>
        /// The physical input that triggers <paramref name="id"/>: the id itself when it already is a physical input, otherwise where
        /// this game's native layout puts that action. Null for an unknown action or a combo (which has no single input).
        /// </summary>
        public string ResolvePhysical(string id)
        {
            string physical = Inputs.Canonical(id);
            if (physical != null && Inputs.IsButton(physical)) return physical;
            NativeBinding b = NativeBindingFor(id);
            return b != null && !b.IsCombo ? Inputs.Canonical(b.Primary) : null;
        }

        /// <summary>What the game does on a physical input, for example "Jump", or "Ping  ·  hold: Ping Wheel"; null when nothing is bound to it.</summary>
        public string NativeLabel(string physicalInput)
        {
            string key = Inputs.Canonical(physicalInput);
            return key != null && Native.TryGetValue(key, out string text) ? text : null;
        }

        private IReadOnlyDictionary<string, string> BuildNative()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, NativeBinding> preset = DefaultPreset();
            foreach (string input in Inputs.Buttons)
            {
                var taps = new List<string>();
                var holds = new List<string>();
                foreach (GameActionDef action in Actions)   // catalog order decides the wording order
                {
                    if (!preset.TryGetValue(action.Id, out NativeBinding b) || b.IsCombo || !string.Equals(b.Primary, input, StringComparison.OrdinalIgnoreCase)) continue;
                    (b.IsHold ? holds : taps).Add(action.Name);
                }
                if (taps.Count == 0 && holds.Count == 0) continue;
                string text = string.Join(" / ", taps);
                if (holds.Count > 0) text += (text.Length > 0 ? "  ·  " : "") + "hold: " + string.Join(" / ", holds);
                result[input] = text;
            }
            return result;
        }

        /// <summary>Upper-cases ids, drops unusable entries and fills in <see cref="NativeBinding.Inputs"/>. Unverified games expose nothing.</summary>
        internal void Normalize()
        {
            if (!Verified) { Actions = new(); NativeLayouts = new(); native = null; return; }
            Actions = Actions.Where(a => !string.IsNullOrWhiteSpace(a.Id)).Select(a => new GameActionDef { Id = ActionIds.Normalize(a.Id), Name = string.IsNullOrWhiteSpace(a.Name) ? a.Id : a.Name, Category = a.Category ?? "" }).ToList();
            var fixedLayouts = new Dictionary<string, Dictionary<string, NativeBinding>>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in NativeLayouts)
            {
                var map = new Dictionary<string, NativeBinding>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in preset.Value)
                {
                    var inputs = new List<string>();
                    if (!string.IsNullOrWhiteSpace(entry.Value.Input)) inputs.Add(entry.Value.Input);
                    inputs.AddRange(entry.Value.Inputs ?? new());
                    inputs = inputs.Select(i => Inputs.Canonical(LegacyIds.ToCanonical(i) ?? i)).Where(i => i != null).Distinct().ToList();
                    if (inputs.Count == 0) continue;
                    map[ActionIds.Normalize(entry.Key)] = new NativeBinding { Input = inputs[0], Inputs = inputs, Gesture = string.IsNullOrWhiteSpace(entry.Value.Gesture) ? "tap" : entry.Value.Gesture };
                }
                fixedLayouts[preset.Key] = map;
            }
            NativeLayouts = fixedLayouts;
            native = null;
        }
    }

    /// <summary>The list of supported games plus the global fallback ("Other").</summary>
    internal static class GameCatalog
    {
        public const string GenericId = "generic";
        public const string ResourcePrefix = "GamepadEnhancer.Games.";

        private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9_-]{0,39}$", RegexOptions.Compiled);

        /// <summary>
        /// The "Default" config: PS4 to keyboard + mouse for any game. It is also the fallback for a game that ships no layout of its own.
        /// </summary>
        public static GameDefinition Generic { get; } = new GameDefinition
        {
            Id = GenericId,
            Name = "Default",
            Controller = ControllerLayouts.DefaultId,
            Verified = true,
            Source = "Built-in keyboard + mouse default (docs/MAPPINGS.md). Any game without its own layout uses it.",
            Layout = "n/a",
            ProfileResource = DefaultProfile.ResourceName
        };

        private static IReadOnlyList<GameDefinition> all;
        public static IReadOnlyList<GameDefinition> All => all ??= LoadEmbedded();

        public static bool IsValidId(string id) => id != null && IdPattern.IsMatch(id);

        /// <summary>The game with this id (case-insensitive), or the generic fallback when unknown.</summary>
        public static GameDefinition Get(string id) =>
            All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Generic;

        /// <summary>Parses one game file; null (and an error) when it is not valid JSON or its id is unusable.</summary>
        public static GameDefinition Parse(string json, out string error)
        {
            error = null;
            try
            {
                GameDefinition g = JsonSerializer.Deserialize<GameDefinition>(json);
                if (g == null) { error = "empty game file"; return null; }
                if (!IsValidId(g.Id)) { error = "invalid game id"; return null; }
                if (string.IsNullOrWhiteSpace(g.Name)) g.Name = g.Id;
                g.Normalize();
                return g;
            }
            catch (JsonException ex) { error = ex.Message; return null; }
        }

        private static IReadOnlyList<GameDefinition> LoadEmbedded()
        {
            var list = new List<GameDefinition>();
            var asm = typeof(GameCatalog).Assembly;
            foreach (string resource in asm.GetManifestResourceNames()
                         .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                try
                {
                    using Stream stream = asm.GetManifestResourceStream(resource);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    GameDefinition g = Parse(reader.ReadToEnd(), out _);
                    if (g != null && !list.Any(x => string.Equals(x.Id, g.Id, StringComparison.OrdinalIgnoreCase))) list.Add(g);
                }
                catch { /* a broken game file must not stop the app: that game is just missing */ }
            }
            // the Default configs first, then the games alphabetically
            list.Sort((a, b) =>
            {
                int ga = a.Id.StartsWith("default", StringComparison.Ordinal) ? 0 : 1, gb = b.Id.StartsWith("default", StringComparison.Ordinal) ? 0 : 1;
                return ga != gb ? ga.CompareTo(gb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            list.Insert(0, Generic);   // "Default (PS4)" is the very first entry
            return list;
        }
    }

    internal enum ProfileSource { User, BuiltIn, Global }

    /// <summary>Which profile a game starts with: the user's saved customization, else the game's built-in default, else the global fallback.</summary>
    internal static class GameProfiles
    {
        /// <summary>The game's built-in default profile, or null when the game ships none (it then falls back to the global profile).</summary>
        public static InputProfile BuiltIn(GameDefinition game)
        {
            if (game == null || string.IsNullOrEmpty(game.ProfileResource)) return null;
            InputProfile p = DefaultProfile.LoadResource(game.ProfileResource);
            if (p != null) p.Name = game.DisplayName;
            return p;
        }

        /// <summary>Priority 1: saved user customization; 2: built-in default for the game; 3: the global fallback (the Default config).</summary>
        public static InputProfile Resolve(GameDefinition game, GameProfileStore store, Func<InputProfile> global, out ProfileSource source)
        {
            if (game != null && store != null && GameCatalog.IsValidId(game.Id))
            {
                InputProfile user = store.LoadUser(game.Id);
                if (user != null) { source = ProfileSource.User; return user; }
            }
            InputProfile builtIn = BuiltIn(game);
            if (builtIn != null) { source = ProfileSource.BuiltIn; return builtIn; }
            source = ProfileSource.Global;
            return (global != null ? global() : null) ?? DefaultProfile.Create();
        }

        /// <summary>The profile "Restore defaults" returns to: the built-in one, else the global fallback.</summary>
        public static InputProfile Defaults(GameDefinition game, Func<InputProfile> global) =>
            BuiltIn(game) ?? (global != null ? global() : null) ?? DefaultProfile.Create();

        /// <summary>True when two profiles mean the same thing (controller type, mouse look, every binding and paddle).</summary>
        public static bool Equivalent(InputProfile a, InputProfile b)
        {
            if (a == null || b == null) return a == b;
            if (ControllerLayouts.Get(a.ControllerType).Id != ControllerLayouts.Get(b.ControllerType).Id || a.MouseLookEnabled != b.MouseLookEnabled || a.MouseInvertY != b.MouseInvertY
                || a.MouseSensitivityX != b.MouseSensitivityX || a.MouseSensitivityY != b.MouseSensitivityY || a.MouseCurve != b.MouseCurve
                || a.MouseGameDeadZonePercent != b.MouseGameDeadZonePercent || a.MouseReleaseMs != b.MouseReleaseMs)
                return false;
            var ma = ProfileMappings.Resolve(a);
            var mb = ProfileMappings.Resolve(b);
            foreach (string action in ControllerEngine.BindingActions)
            {
                MappingEntry x = ma[action], y = mb[action];
                if (!string.Equals(x.Input, y.Input, StringComparison.OrdinalIgnoreCase) || x.Turbo != y.Turbo || x.TurboHz != y.TurboHz) return false;
            }
            if (!BindingEntries.Equivalent(BindingEntries.Resolve(a.AdvancedBindings), BindingEntries.Resolve(b.AdvancedBindings))) return false;
            PaddleSetting[] pa = PaddleMappings.Resolve(a), pb = PaddleMappings.Resolve(b);
            for (int i = 0; i < PaddleSlots.Count; i++)
                if (pa[i].Enabled != pb[i].Enabled || pa[i].Kind != pb[i].Kind || !string.Equals(pa[i].Input, pb[i].Input, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }

    /// <summary>
    /// Per-game user customizations, one JSON file per game under <c>profiles/games/</c> (same format as every profile, so a file
    /// can be copied, edited or imported). Built-in defaults are never stored here, so they stay separate from user changes and a
    /// change for one game can never touch another game's file.
    /// </summary>
    internal sealed class GameProfileStore
    {
        private readonly string directory;
        public GameProfileStore(string directory) { this.directory = directory ?? throw new ArgumentNullException(nameof(directory)); }

        public string PathFor(string id)
        {
            if (!GameCatalog.IsValidId(id)) throw new ArgumentException("Invalid game id.", nameof(id));
            return Path.Combine(directory, id + ".json");
        }

        public bool HasUser(string id) => GameCatalog.IsValidId(id) && File.Exists(PathFor(id));

        /// <summary>The saved customization (validated and normalized like an import), or null when none exists or it is unreadable.</summary>
        public InputProfile LoadUser(string id)
        {
            try
            {
                if (!HasUser(id)) return null;
                return ProfileImport.Parse(File.ReadAllText(PathFor(id)), out _, out _);
            }
            catch { return null; }
        }

        /// <summary>Writes atomically (temp file, then replace). Returns false instead of throwing when the folder is not writable.</summary>
        public bool SaveUser(string id, InputProfile profile)
        {
            try
            {
                string path = PathFor(id);
                Directory.CreateDirectory(directory);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch { return false; }
        }

        public bool DeleteUser(string id)
        {
            try
            {
                if (!HasUser(id)) return false;
                File.Delete(PathFor(id));
                return true;
            }
            catch { return false; }
        }
    }
}
