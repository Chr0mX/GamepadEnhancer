using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamepadEnhancer
{
    /// <summary>
    /// One controller layout: how the canonical physical inputs are <em>labelled</em> for a particular kind of controller, and which
    /// virtual device it drives. This is the only place brand names live (in <c>Defaults/Controllers/*.json</c>); the rest of the
    /// app asks for a label and gets "Cross" or "A" depending on the layout. Adding a controller that behaves like an existing
    /// virtual device (for example a DualSense that is presented as a DualShock 4) is one new JSON file.
    /// </summary>
    internal sealed class ControllerLayoutDef
    {
        public string Id { get; set; } = "";
        /// <summary>Short name shown in the controller selector.</summary>
        public string Name { get; set; } = "";
        /// <summary>Long name used in titles.</summary>
        public string Title { get; set; } = "";
        /// <summary>Other ids that mean this layout (older profiles stored "PS4" and "Xbox 360").</summary>
        public List<string> Aliases { get; set; } = new();
        /// <summary>The virtual device this layout is output through: "Xbox360" or "DualShock4".</summary>
        public string Device { get; set; } = "";
        /// <summary>Canonical input id to its label on this controller. Inputs missing here use the neutral label.</summary>
        public Dictionary<string, string> Labels { get; set; } = new();
        /// <summary>Inputs this controller does not have (they are ignored, and drawn as unavailable).</summary>
        public List<string> Unsupported { get; set; } = new();
        /// <summary>"symbols" draws the four face buttons as shapes, "letters" draws their labels.</summary>
        public string FaceStyle { get; set; } = "letters";
        /// <summary>Canonical face-button id to an "#RRGGBB" colour.</summary>
        public Dictionary<string, string> FaceColors { get; set; } = new();

        public PadFamily Family => Enum.TryParse(Device, true, out PadFamily f) ? f : PadFamily.DualShock4;

        public string Label(string inputId)
        {
            string id = Inputs.Canonical(inputId) ?? inputId;
            return id != null && Labels.TryGetValue(id, out string text) && !string.IsNullOrWhiteSpace(text) ? text : Inputs.NeutralLabel(id);
        }

        public bool Supports(string inputId) =>
            !Unsupported.Any(u => string.Equals(u, inputId, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => Name;
    }

    /// <summary>The known controller layouts, loaded from embedded <c>Defaults/Controllers/*.json</c>.</summary>
    internal static class ControllerLayouts
    {
        public const string ResourcePrefix = "GamepadEnhancer.Controllers.";
        public const string DefaultId = "dualshock4";

        private static IReadOnlyList<ControllerLayoutDef> all;
        public static IReadOnlyList<ControllerLayoutDef> All => all ??= LoadEmbedded();

        /// <summary>The layout with this id or alias (case-insensitive), else the default layout.</summary>
        public static ControllerLayoutDef Get(string idOrAlias)
        {
            if (!string.IsNullOrWhiteSpace(idOrAlias))
                foreach (ControllerLayoutDef l in All)
                    if (string.Equals(l.Id, idOrAlias, StringComparison.OrdinalIgnoreCase)
                        || l.Aliases.Any(a => string.Equals(a, idOrAlias, StringComparison.OrdinalIgnoreCase)))
                        return l;
            return All.FirstOrDefault(l => l.Id == DefaultId) ?? All.First();
        }

        /// <summary>The first layout that drives this virtual device.</summary>
        public static ControllerLayoutDef ForDevice(PadFamily family) =>
            All.FirstOrDefault(l => l.Family == family) ?? Get(null);

        /// <summary>Parses one layout file; null (with an error) when it is unusable.</summary>
        public static ControllerLayoutDef Parse(string json, out string error)
        {
            error = null;
            try
            {
                ControllerLayoutDef l = JsonSerializer.Deserialize<ControllerLayoutDef>(json);
                if (l == null || string.IsNullOrWhiteSpace(l.Id)) { error = "missing id"; return null; }
                if (!Enum.TryParse(l.Device, true, out PadFamily _)) { error = $"unknown device \"{l.Device}\""; return null; }
                if (string.IsNullOrWhiteSpace(l.Name)) l.Name = l.Id;
                if (string.IsNullOrWhiteSpace(l.Title)) l.Title = l.Name;
                // keys are canonical ids; accept any casing in the file
                l.Labels = Normalize(l.Labels);
                l.FaceColors = Normalize(l.FaceColors);
                l.Unsupported = l.Unsupported.Select(u => Inputs.Canonical(u) ?? u).ToList();
                return l;
            }
            catch (JsonException ex) { error = ex.Message; return null; }
        }

        private static Dictionary<string, string> Normalize(Dictionary<string, string> d) =>
            (d ?? new()).ToDictionary(p => Inputs.Canonical(p.Key) ?? p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        private static IReadOnlyList<ControllerLayoutDef> LoadEmbedded()
        {
            var list = new List<ControllerLayoutDef>();
            var asm = typeof(ControllerLayouts).Assembly;
            foreach (string resource in asm.GetManifestResourceNames()
                         .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                try
                {
                    using Stream stream = asm.GetManifestResourceStream(resource);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    ControllerLayoutDef l = Parse(reader.ReadToEnd(), out _);
                    if (l != null && !list.Any(x => string.Equals(x.Id, l.Id, StringComparison.OrdinalIgnoreCase))) list.Add(l);
                }
                catch { /* a broken layout file is skipped */ }
            }
            // the default layout first, then the rest alphabetically by name
            list.Sort((a, b) => a.Id == DefaultId ? -1 : b.Id == DefaultId ? 1 : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0) list.Add(new ControllerLayoutDef { Id = DefaultId, Name = DefaultId, Title = DefaultId, Device = nameof(PadFamily.DualShock4) });
            return list;
        }
    }
}
