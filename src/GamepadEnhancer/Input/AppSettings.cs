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

        /// <summary>The selected game's id (see GameCatalog); apex by default, which is what the app used to start on.</summary>
        public string ActiveGame { get; set; } = "apex";
        /// <summary>Which hardware paddle (0..3 = Paddle1..4) feeds each of the four paddle slots; see PaddleSlots.NormalizeSourceMap.</summary>
        public int[] PaddleSourceIndex { get; set; } = { 0, 1, 2, 3 };

        /// <summary>Revision of the shipped default mapping already applied to profiles/Default.json (see DefaultProfile.MappingVersion).</summary>
        public int DefaultMappingVersion { get; set; }

        /// <summary>Input blocking: swallow the mapped physical keyboard/mouse input while a chosen game is focused. Off by default.</summary>
        public bool BlockEnabled { get; set; }
        /// <summary>Process names (comma / semicolon / newline separated, ".exe" optional) that blocking applies to.</summary>
        public string BlockProcesses { get; set; } = "";
        /// <summary>Virtual-key code of the toggle / panic hotkey (default Scroll Lock).</summary>
        public int BlockHotkeyVk { get; set; } = 0x91;

        /// <summary>When blocking is on, swallow mapped keyboard keys. On by default so existing setups behave as before.</summary>
        public bool BlockKeyboard { get; set; } = true;
        /// <summary>When blocking is on, swallow mapped mouse buttons, wheel and (with mouse look) movement.</summary>
        public bool BlockMouse { get; set; } = true;

        /// <summary>Live controller monitor (Monitor tab and overlay window). All app-wide, not per profile.</summary>
        public bool MonitorEnabled { get; set; } = true;
        /// <summary>"Physical", "Processed" or "Final".</summary>
        public string MonitorView { get; set; } = "Final";
        /// <summary>Size of the drawn controller, percent of the base 440x300 (50..200).</summary>
        public int MonitorScalePercent { get; set; } = 100;
        /// <summary>UI refresh rate in Hz (30..144). Independent of the output rate.</summary>
        public int MonitorRefreshHz { get; set; } = 60;
        public bool MonitorCompact { get; set; }
        public bool MonitorAlwaysOnTop { get; set; }
        /// <summary>Overlay window without frame or background (only the controller is drawn).</summary>
        public bool MonitorOverlayMode { get; set; }

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
