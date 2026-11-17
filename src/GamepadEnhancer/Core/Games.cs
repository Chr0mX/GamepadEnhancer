using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamepadEnhancer
{
    /// <summary>
    /// One supported game. Loaded from an embedded <c>Defaults/Games/&lt;id&gt;.json</c>, so a game's data can be updated
    /// without touching code and a new game is one new file. <see cref="Native"/> holds what the game's own default
    /// controller layout does on each pad button (display only: the game owns those assignments). <see cref="ProfileResource"/>
    /// optionally names an embedded profile with this app's default PC-input bindings for the game.
    /// </summary>
    internal sealed class GameDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Where the native labels come from (a verifiable description, never a guess).</summary>
        public string Source { get; set; } = "";
        /// <summary>False while the native layout has not been transcribed from a real source; such a game shows no labels.</summary>
        public bool Verified { get; set; }
        public string Layout { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProfileResource { get; set; }
        /// <summary>Pad action name (<see cref="ControllerEngine.BindingActions"/>) -> what the game does on it.</summary>
        public Dictionary<string, string> Native { get; set; } = new();

        public string NativeLabel(string action) =>
            Native != null && action != null && Native.TryGetValue(action, out string text) && !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    /// <summary>The list of supported games plus the global fallback ("Other").</summary>
    internal static class GameCatalog
    {
        public const string GenericId = "generic";
        public const string ResourcePrefix = "GamepadEnhancer.Games.";

        private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9_-]{0,39}$", RegexOptions.Compiled);

        /// <summary>Unsupported games use this: its built-in default is the global fallback profile (profiles/Default.json).</summary>
        public static GameDefinition Generic { get; } = new GameDefinition
        {
            Id = GenericId,
            Name = "Other game (global default)",
            Verified = true,
            Source = "Global fallback: your profiles/Default.json (PS4, nothing bound unless you remapped it).",
            Layout = "n/a"
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
                g.Native = new Dictionary<string, string>(g.Native ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                if (!g.Verified) g.Native.Clear();   // unverified data is never shown
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
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(Generic);
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
            if (p != null) p.Name = game.Name;
            return p;
        }

        /// <summary>Priority 1: saved user customization; 2: built-in default for the game; 3: the global fallback.</summary>
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
            if (a.ControllerType != b.ControllerType || a.MouseLookEnabled != b.MouseLookEnabled || a.MouseInvertY != b.MouseInvertY
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
