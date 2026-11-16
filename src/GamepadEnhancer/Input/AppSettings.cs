using System;
using System.IO;
using System.Text.Json;

namespace GamepadEnhancer
{
    /// <summary>
    /// App-wide settings (not per profile), stored as settings.json next to the executable.
    /// Currently holds the selected input capture method.
    /// </summary>
    internal sealed class AppSettings
    {
        /// <summary>"LowLevelHooks" (default) or "RawInput".</summary>
        public string InputCaptureMethod { get; set; } = "LowLevelHooks";

        /// <summary>Network API (MAKCU-style text commands over TCP). Off by default.</summary>
        public bool NetworkApiEnabled { get; set; }
        public int NetworkApiPort { get; set; } = 5555;
        /// <summary>True = listen on all interfaces (LAN); false = this PC only.</summary>
        public bool NetworkApiBindAll { get; set; } = true;
        /// <summary>Shared secret a client must send as "auth &lt;token&gt;". Generated on first enable. Never logged.</summary>
        public string NetworkApiToken { get; set; } = "";

        /// <summary>Send mode: Connect sends this app's pad to another PC (its Network API) instead of creating a local ViGEm pad.</summary>
        public bool RemoteSendEnabled { get; set; }
        public string RemoteHost { get; set; } = "";
        public int RemotePort { get; set; } = 5555;
        /// <summary>Token of the other PC's Network API (stored in plain text in settings.json, like the receiver's own token).</summary>
        public string RemoteToken { get; set; } = "";

        /// <summary>Mouse button UDP sender: streams this PC's mouse buttons to another PC. Off by default.</summary>
        public bool MouseUdpEnabled { get; set; }
        public string MouseUdpHost { get; set; } = "";
        public int MouseUdpPort { get; set; } = 5602;
        /// <summary>Optional shared tag secret (plain text in settings.json); must match the receiver's token.</summary>
        public string MouseUdpToken { get; set; } = "";

        public static string NewToken()
        {
            const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[24];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(chars);
        }

        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static AppSettings Load(string path = null)
        {
            try
            {
                path ??= DefaultPath;
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
            catch { /* unreadable or corrupt: fall back to defaults */ }
            return new AppSettings();
        }

        /// <summary>Returns false (instead of throwing) if the file can't be written, e.g. a read-only install folder.</summary>
        public bool Save(string path = null)
        {
            try
            {
                File.WriteAllText(path ?? DefaultPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch { return false; }
        }
    }
}
