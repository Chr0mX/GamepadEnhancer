using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GamepadEnhancer
{
    /// <summary>
    /// Saved profile (JSON in the "profiles" folder). Older files may still carry the removed drift, filtering, recoil and
    /// disengage fields; they are ignored when loading and are not written back.
    /// </summary>
    internal sealed class InputProfile
    {
        public string Name { get; set; } = "Default";
        public string ControllerType { get; set; } = "PS4";
        /// <summary>Mouse movement drives the right stick (needs Raw Input; works with either capture method). Off by default.</summary>
        public bool MouseLookEnabled { get; set; }
        public double MouseSensitivityX { get; set; } = 8.0;
        public double MouseSensitivityY { get; set; } = 8.0;
        public double MouseCurve { get; set; } = 1.0;
        /// <summary>The game's inner dead zone in percent (0..50).</summary>
        public int MouseGameDeadZonePercent { get; set; }
        public int MouseReleaseMs { get; set; } = 30;
        public bool MouseInvertY { get; set; }
        public Dictionary<string, string> KeyBindings { get; set; } = new();
        public Dictionary<string, bool> TurboEnabled { get; set; } = new();
        public Dictionary<string, int> TurboHz { get; set; } = new();
    }
}

namespace GamepadEnhancer
{
    /// <summary>
    /// The default profile is a JSON file shipped inside the app (<c>Defaults/Default.json</c>, embedded). On first run it is
    /// also written to <c>profiles/Default.json</c> so it shows up in the profile list, and the app starts with
    /// <c>profiles/Default.json</c> when it exists and is valid (so what you save as "Default" is what you get on launch),
    /// otherwise with the embedded one. Missing fields in OLD profile files still fall back to the original property
    /// defaults in <see cref="InputProfile"/>, so loading an old profile never gains drift or filtering it did not have.
    /// </summary>
    internal static class DefaultProfile
    {
        public const string ResourceName = "GamepadEnhancer.Default.json";
        public const string FileName = "Default.json";

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

        /// <summary>Writes profiles/Default.json from the embedded profile if it does not exist yet. Returns true if it created it.</summary>
        public static bool EnsureFile(string profilesDirectory)
        {
            try
            {
                string path = Path.Combine(profilesDirectory, FileName);
                if (File.Exists(path)) return false;
                Directory.CreateDirectory(profilesDirectory);
                File.WriteAllText(path, JsonSerializer.Serialize(Create(), new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch { return false; }
        }

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
