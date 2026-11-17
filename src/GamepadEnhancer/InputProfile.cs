using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamepadEnhancer
{
    /// <summary>One pad action's binding in a profile. <see cref="Kind"/> is only there for people reading the file; it is derived from <see cref="Input"/> on load.</summary>
    internal sealed class MappingEntry
    {
        /// <summary>The bound input: a keyboard key ("Space"), a mouse button ("Mouse X1"), a wheel notch ("Wheel Up") or "None".</summary>
        public string Input { get; set; } = "None";
        /// <summary>"Keyboard", "MouseButton", "MouseWheel" or "None". Informational.</summary>
        public string Kind { get; set; } = "None";
        public bool Turbo { get; set; }
        public int TurboHz { get; set; } = 12;
    }

    /// <summary>
    /// Saved profile (JSON in the "profiles" folder). Bindings live in <see cref="Mappings"/>, keyed by action name; the right
    /// stick is mouse movement (the MouseLook fields). Older files may carry <c>KeyBindings</c>/<c>TurboEnabled</c>/<c>TurboHz</c>
    /// instead of <c>Mappings</c>, and the removed drift, filtering, recoil and disengage fields; those still load (the old
    /// binding dictionaries are used when <c>Mappings</c> is absent, the removed fields are ignored) and are not written back.
    /// </summary>
    internal sealed class InputProfile
    {
        public string Name { get; set; } = "Default";
        /// <summary>Profile layout version: 2 = canonical ids (see <see cref="Inputs"/>). Older files have none; their ids are converted when read.</summary>
        public int Schema { get; set; }
        /// <summary>The controller layout this profile targets: a layout id such as "dualshock4" (older files store "PS4" / "Xbox 360", still read).</summary>
        public string ControllerType { get; set; } = ControllerLayouts.DefaultId;
        /// <summary>The game whose action catalog the <c>Mappings</c> keys may use (JUMP, RELOAD, ...); null = controller inputs only.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Game { get; set; }
        /// <summary>Mouse movement drives the right stick (needs Raw Input; works with either capture method).</summary>
        public bool MouseLookEnabled { get; set; }
        public double MouseSensitivityX { get; set; } = 8.0;
        public double MouseSensitivityY { get; set; } = 8.0;
        public double MouseCurve { get; set; } = 1.0;
        /// <summary>The game's inner dead zone in percent (0..50).</summary>
        public int MouseGameDeadZonePercent { get; set; }
        public int MouseReleaseMs { get; set; } = 30;
        public bool MouseInvertY { get; set; }

        /// <summary>Rear paddle mappings (LeftUpper, LeftLower, RightUpper, RightLower). Null in profiles written before paddle support.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, PaddleEntry> Paddles { get; set; }

        /// <summary>Action name -> binding. Null only in profiles written by older builds.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, MappingEntry> Mappings { get; set; }

        // Legacy binding dictionaries (read-only fallback, never written).
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string> KeyBindings { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, bool> TurboEnabled { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, int> TurboHz { get; set; }
    }

    /// <summary>Reading and building a profile's bindings. Pure.</summary>
    internal static class ProfileMappings
    {
        private sealed class Raw { public string Input = "None"; public bool Turbo; public int Hz = 12; }

        /// <summary>
        /// The binding for every physical input (the engine's buttons), keyed by canonical id and always complete. A profile may key its
        /// bindings by <b>physical input</b> (FACE_SOUTH) or by <b>game action</b> (JUMP); a game action is resolved through the profile's
        /// game and that game's native layout, so the same profile works whatever the controller is. Older profiles keyed by
        /// "A / Cross" style ids are converted. A physical entry wins over an action that lands on the same input; two actions on one
        /// input keep the first (alphabetically). Unknown or unusable entries are cleared and reported in <paramref name="warnings"/>.
        /// </summary>
        public static Dictionary<string, MappingEntry> Resolve(InputProfile p, List<string> warnings = null)
        {
            GameDefinition game = GameCatalog.Get(p.Game);
            var raw = CollectRaw(p);

            var winners = new Dictionary<string, (string Source, Raw Entry)>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in raw.Keys.OrderBy(k => k, StringComparer.Ordinal).Where(k => !Inputs.IsButton(k)))
            {
                string physical = game.ResolvePhysical(key);
                if (physical == null)
                {
                    if (Inputs.IsPaddle(key) || key == Inputs.RightStickMove) continue;   // not a button binding
                    warnings?.Add($"{key}: not an action of {game.DisplayName} and not a controller input; ignored");
                    continue;
                }
                if (winners.TryGetValue(physical, out var other))
                {
                    warnings?.Add($"{key} and {other.Source} both drive {physical}; {other.Source} is used");
                    continue;
                }
                winners[physical] = (key, raw[key]);
            }
            foreach (string key in raw.Keys.Where(Inputs.IsButton))
            {
                string physical = Inputs.Canonical(key);
                if (winners.TryGetValue(physical, out var other)) warnings?.Add($"{key} overrides {other.Source}");
                winners[physical] = (physical, raw[key]);
            }

            var result = new Dictionary<string, MappingEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string action in ControllerEngine.BindingActions)
            {
                string input = "None";
                bool turbo = false;
                int hz = 12;
                string label = action;
                if (winners.TryGetValue(action, out var w)) { input = w.Entry.Input; turbo = w.Entry.Turbo; hz = w.Entry.Hz; label = w.Source; }

                string canonical = InputChoices.Canonical(input);
                if (canonical == "None" && !string.Equals(input, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(input))
                    warnings?.Add($"{label}: unknown input \"{input}\" was cleared");
                else if (!InputChoices.IsAllowed(action, canonical))
                {
                    warnings?.Add($"{label}: \"{input}\" is not allowed here (keyboard keys only) and was cleared");
                    canonical = "None";
                }
                result[action] = new MappingEntry { Input = canonical, Kind = KindName(canonical), Turbo = turbo, TurboHz = Math.Clamp(hz, 1, 30) };
            }
            return result;
        }

        /// <summary>Every binding in the profile under its canonical key (legacy ids converted), from <c>Mappings</c> or the oldest dictionaries.</summary>
        private static Dictionary<string, Raw> CollectRaw(InputProfile p)
        {
            var raw = new Dictionary<string, Raw>(StringComparer.OrdinalIgnoreCase);
            if (p.Mappings != null)
            {
                foreach (var pair in p.Mappings)
                    if (pair.Value != null) raw[ActionIds.Normalize(pair.Key)] = new Raw { Input = pair.Value.Input ?? "None", Turbo = pair.Value.Turbo, Hz = pair.Value.TurboHz };
                return raw;
            }
            if (p.KeyBindings != null)
                foreach (var pair in p.KeyBindings)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                    string id = ActionIds.Normalize(pair.Key);
                    raw[id] = new Raw { Input = pair.Value };
                }
            if (p.TurboEnabled != null)
                foreach (var pair in p.TurboEnabled)
                    if (raw.TryGetValue(ActionIds.Normalize(pair.Key), out Raw r)) r.Turbo = pair.Value;
            if (p.TurboHz != null)
                foreach (var pair in p.TurboHz)
                    if (raw.TryGetValue(ActionIds.Normalize(pair.Key), out Raw r)) r.Hz = pair.Value;
            return raw;
        }

        /// <summary>The ids found in a profile that are neither controller inputs, legacy ids nor actions of its game (they are ignored when loading).</summary>
        public static IEnumerable<string> UnknownIds(InputProfile p)
        {
            GameDefinition game = GameCatalog.Get(p.Game);
            foreach (string key in CollectRaw(p).Keys)
                if (!Inputs.IsKnown(key) && game.ResolvePhysical(key) == null) yield return key;
        }

        /// <summary>Builds the writable <c>Mappings</c> for a profile from (action, input, turbo, hz) rows.</summary>
        public static Dictionary<string, MappingEntry> Build(IEnumerable<(string Action, string Input, bool Turbo, int Hz)> rows)
        {
            var map = new Dictionary<string, MappingEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                string input = InputChoices.Canonical(r.Input);
                map[r.Action] = new MappingEntry { Input = input, Kind = KindName(input), Turbo = r.Turbo, TurboHz = Math.Clamp(r.Hz, 1, 30) };
            }
            return map;
        }

        public static string KindName(string input) => InputChoices.KindOf(input) switch
        {
            InputKind.Keyboard => "Keyboard",
            InputKind.MouseButton => "MouseButton",
            InputKind.MouseWheel => "MouseWheel",
            _ => "None"
        };

    }

    /// <summary>Validation for a profile file picked by the user (import). Pure, so it can be unit tested.</summary>
    internal static class ProfileImport
    {
        public const int MaxBytes = 256 * 1024;

        /// <summary>
        /// Parses and normalizes a profile. Returns null (and an <paramref name="error"/>) when it is too large or not a profile;
        /// otherwise a profile whose bindings are all valid, with <paramref name="warnings"/> listing everything that was cleared.
        /// </summary>
        public static InputProfile Parse(string json, out string error, out List<string> warnings)
        {
            warnings = new List<string>();
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "The file is empty."; return null; }
            if (json.Length > MaxBytes) { error = "The file is too large to be a profile."; return null; }

            InputProfile p;
            try { p = JsonSerializer.Deserialize<InputProfile>(json); }
            catch (JsonException) { error = "The file is not valid JSON."; return null; }
            if (p == null) { error = "The file does not contain a profile."; return null; }

            int unknown = ProfileMappings.UnknownIds(p).Count();
            if (unknown > 0) warnings.Add($"{unknown} unknown action(s) were ignored");

            var resolved = ProfileMappings.Resolve(p, warnings);
            p.Mappings = resolved;
            p.KeyBindings = null;
            p.TurboEnabled = null;
            p.TurboHz = null;

            if (p.Paddles != null) p.Paddles = PaddleMappings.Build(PaddleMappings.Resolve(p, warnings));

            p.Schema = 2;
            p.ControllerType = ControllerLayouts.Get(p.ControllerType).Id;
            p.MouseSensitivityX = Math.Clamp(p.MouseSensitivityX, 0.5, 100.0);
            p.MouseSensitivityY = Math.Clamp(p.MouseSensitivityY, 0.5, 100.0);
            p.MouseCurve = Math.Clamp(p.MouseCurve, 0.5, 4.0);
            p.MouseGameDeadZonePercent = Math.Clamp(p.MouseGameDeadZonePercent, 0, 50);
            p.MouseReleaseMs = Math.Clamp(p.MouseReleaseMs, 0, 500);
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Imported";
            return p;
        }
    }

    /// <summary>
    /// Two profiles ship inside the app as embedded resources: <b>Default</b> (<c>Defaults/Default.json</c>: a PS4 pad with
    /// nothing bound, to be remapped) and <b>Apex</b> (<c>Defaults/Apex.json</c>: the keyboard + mouse layout in
    /// docs/MAPPINGS.md). Default is written to <c>profiles/Default.json</c> on first run and is an ordinary, editable
    /// profile; Apex is built in: it is always read from the app, never from <c>profiles/</c>, and cannot be overwritten.
    /// The app starts on Apex. When the shipped default changes (<see cref="MappingVersion"/>), an existing
    /// <c>Default.json</c> is replaced once and the old file is kept as a backup.
    /// </summary>
    internal static class DefaultProfile
    {
        public const string ResourceName = "GamepadEnhancer.Default.json";
        public const string ApexResourceName = "GamepadEnhancer.Apex.json";
        public const string FileName = "Default.json";
        public const string ApexName = "Apex";

        /// <summary>Revision of the shipped default profile; stored in settings.json once the upgrade has been applied.</summary>
        public const int MappingVersion = 5;

        /// <summary>The embedded Default profile: PS4, nothing bound.</summary>
        public static InputProfile Create() => Load(ResourceName, new InputProfile { Name = "Default" });

        /// <summary>The embedded Apex profile (falls back to an empty one named Apex only if the resource is missing).</summary>
        public static InputProfile CreateApex() => Load(ApexResourceName, new InputProfile { Name = ApexName });

        private static InputProfile Load(string resource, InputProfile fallback) => LoadResource(resource) ?? fallback;

        /// <summary>An embedded profile by resource name, or null when it is missing or unreadable.</summary>
        public static InputProfile LoadResource(string resource)
        {
            try
            {
                using Stream stream = typeof(DefaultProfile).Assembly.GetManifestResourceStream(resource);
                if (stream != null) return JsonSerializer.Deserialize<InputProfile>(stream);
            }
            catch { }
            return null;
        }

        public static bool ShouldUpgradeDefault(int storedVersion) => storedVersion < MappingVersion;

        /// <summary>"Default.json.bak", or ".bak2", ".bak3", ... the first one that does not exist yet (a backup is never overwritten).</summary>
        public static string NextBackupName(Func<string, bool> exists)
        {
            string name = FileName + ".bak";
            for (int i = 2; exists(name); i++) name = FileName + ".bak" + i;
            return name;
        }

        /// <summary>Replaces <c>Default.json</c> with the shipped one, keeping an existing file as a backup. Returns the backup path or null.</summary>
        public static string UpgradeDefaultFile(string profilesDirectory)
        {
            string backup = null;
            try
            {
                Directory.CreateDirectory(profilesDirectory);
                string path = Path.Combine(profilesDirectory, FileName);
                if (File.Exists(path))
                {
                    backup = Path.Combine(profilesDirectory, NextBackupName(n => File.Exists(Path.Combine(profilesDirectory, n))));
                    File.Copy(path, backup);
                }
                WriteDefault(path);
            }
            catch { /* read-only folder etc.: the embedded default is still used */ }
            return backup;
        }

        /// <summary>
        /// Startup: apply the one-time default upgrade when needed, else make sure a first-run file exists.
        /// Returns true when the stored version should be updated (the caller saves settings).
        /// </summary>
        public static bool PrepareStartup(string profilesDirectory, int storedVersion)
        {
            if (ShouldUpgradeDefault(storedVersion))
            {
                UpgradeDefaultFile(profilesDirectory);
                return true;
            }
            EnsureFile(profilesDirectory);
            return false;
        }

        /// <summary>Writes profiles/Default.json from the embedded profile if it does not exist yet. Returns true if it created it.</summary>
        public static bool EnsureFile(string profilesDirectory)
        {
            try
            {
                string path = Path.Combine(profilesDirectory, FileName);
                if (File.Exists(path)) return false;
                Directory.CreateDirectory(profilesDirectory);
                WriteDefault(path);
                return true;
            }
            catch { return false; }
        }

        private static void WriteDefault(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(Create(), new JsonSerializerOptions { WriteIndented = true }));

        /// <summary>profiles/Default.json (the user's editable copy) if it loads, otherwise the embedded Default.</summary>
        public static InputProfile LoadDefaultFile(string profilesDirectory)
        {
            try
            {
                string path = Path.Combine(profilesDirectory, FileName);
                if (File.Exists(path))
                {
                    InputProfile profile = JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(path));
                    if (profile != null) return profile;
                }
            }
            catch { /* unreadable or corrupt: use the embedded default */ }
            return Create();
        }
    }
}
