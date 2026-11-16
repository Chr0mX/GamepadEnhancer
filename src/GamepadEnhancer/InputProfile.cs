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
        public string ControllerType { get; set; } = "PS4";
        /// <summary>Mouse movement drives the right stick (needs Raw Input; works with either capture method).</summary>
        public bool MouseLookEnabled { get; set; }
        public double MouseSensitivityX { get; set; } = 8.0;
        public double MouseSensitivityY { get; set; } = 8.0;
        public double MouseCurve { get; set; } = 1.0;
        /// <summary>The game's inner dead zone in percent (0..50).</summary>
        public int MouseGameDeadZonePercent { get; set; }
        public int MouseReleaseMs { get; set; } = 30;
        public bool MouseInvertY { get; set; }

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
        /// <summary>The binding for every action: from <c>Mappings</c> when present, else from the legacy dictionaries. Never null; unknown inputs become "None".</summary>
        public static Dictionary<string, MappingEntry> Resolve(InputProfile p, List<string> warnings = null)
        {
            var result = new Dictionary<string, MappingEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string action in ControllerEngine.BindingActions)
            {
                string input = "None";
                bool turbo = false;
                int hz = 12;
                if (p.Mappings != null)
                {
                    if (TryGet(p.Mappings, action, out MappingEntry e) && e != null)
                    {
                        input = e.Input ?? "None";
                        turbo = e.Turbo;
                        hz = e.TurboHz;
                    }
                }
                else
                {
                    if (p.KeyBindings != null && TryGet(p.KeyBindings, action, out string k) && !string.IsNullOrWhiteSpace(k)) input = k;
                    if (p.TurboEnabled != null && TryGet(p.TurboEnabled, action, out bool te)) turbo = te;
                    if (p.TurboHz != null && TryGet(p.TurboHz, action, out int h)) hz = h;
                }

                string canonical = InputChoices.Canonical(input);
                if (canonical == "None" && !string.Equals(input, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(input))
                    warnings?.Add($"{action}: unknown input \"{input}\" was cleared");
                else if (!InputChoices.IsAllowed(action, canonical))
                {
                    warnings?.Add($"{action}: \"{input}\" is not allowed here (keyboard keys only) and was cleared");
                    canonical = "None";
                }
                result[action] = new MappingEntry { Input = canonical, Kind = KindName(canonical), Turbo = turbo, TurboHz = Math.Clamp(hz, 1, 30) };
            }
            return result;
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

        private static bool TryGet<T>(Dictionary<string, T> d, string key, out T value)
        {
            if (d.TryGetValue(key, out value)) return true;
            foreach (var pair in d)
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
            value = default;
            return false;
        }
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

            if (p.Mappings != null)
            {
                int unknown = p.Mappings.Keys.Count(k => !ControllerEngine.BindingActions.Contains(k, StringComparer.OrdinalIgnoreCase));
                if (unknown > 0) warnings.Add($"{unknown} unknown action(s) were ignored");
            }

            var resolved = ProfileMappings.Resolve(p, warnings);
            p.Mappings = resolved;
            p.KeyBindings = null;
            p.TurboEnabled = null;
            p.TurboHz = null;

            p.ControllerType = p.ControllerType == "Xbox 360" ? "Xbox 360" : "PS4";
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
    /// The default profile is a JSON file shipped inside the app (<c>Defaults/Default.json</c>, embedded): the keyboard + mouse
    /// layout documented in docs/MAPPINGS.md. On first run it is written to <c>profiles/Default.json</c>, and the app starts
    /// with <c>profiles/Default.json</c> when it exists and is valid, otherwise with the embedded one. When the shipped mapping
    /// changes (<see cref="MappingVersion"/>), an existing <c>Default.json</c> is replaced once and the old file is kept as a backup.
    /// </summary>
    internal static class DefaultProfile
    {
        public const string ResourceName = "GamepadEnhancer.Default.json";
        public const string FileName = "Default.json";

        /// <summary>Revision of the shipped default mapping; stored in settings.json once the upgrade has been applied.</summary>
        public const int MappingVersion = 3;

        /// <summary>The embedded default profile (falls back to the plain property defaults only if the resource is missing).</summary>
        public static InputProfile Create()
        {
            try
            {
                using Stream stream = typeof(DefaultProfile).Assembly.GetManifestResourceStream(ResourceName);
                if (stream != null)
                {
                    InputProfile profile = JsonSerializer.Deserialize<InputProfile>(stream);
                    if (profile != null) return profile;
                }
            }
            catch { }
            return new InputProfile { Name = "Default" };
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

        /// <summary>The profile the app starts with: profiles/Default.json if it loads, otherwise the embedded default.</summary>
        public static InputProfile LoadStartupProfile(string profilesDirectory)
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
