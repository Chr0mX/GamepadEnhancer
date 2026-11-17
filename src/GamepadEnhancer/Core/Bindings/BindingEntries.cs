using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>One output of a binding as stored in a profile file.</summary>
    internal sealed class BindingOutputEntry
    {
        /// <summary>"PadButton", "Key" or "MouseButton".</summary>
        public string Kind { get; set; } = "PadButton";
        public string Name { get; set; } = "None";
        public int Magnitude { get; set; } = 100;
    }

    /// <summary>One advanced binding as stored in a profile file (the optional <c>AdvancedBindings</c> list).</summary>
    internal sealed class BindingEntry
    {
        public string Id { get; set; }
        public string Input { get; set; } = "None";
        /// <summary>Held, Unheld, Press, Release, Double, Tap or Hold.</summary>
        public string Condition { get; set; } = "Held";
        public bool Toggle { get; set; }
        public bool Enabled { get; set; } = true;
        public int DoubleWindowMs { get; set; } = BindingDef.DefaultDoubleWindowMs;
        public int TapMaxMs { get; set; } = BindingDef.DefaultTapMaxMs;
        public int HoldMs { get; set; } = BindingDef.DefaultHoldMs;
        public List<BindingOutputEntry> Outputs { get; set; } = new();
    }

    /// <summary>Reading, validating and writing a profile's advanced bindings. Pure.</summary>
    internal static class BindingEntries
    {
        public const int MaxBindings = 64, MaxOutputs = 8;
        public const int MinDoubleMs = 100, MaxDoubleMs = 1000, MinTapMs = 50, MaxTapMs = 1000, MinHoldMs = 100, MaxHoldMs = 5000;

        public static BindingOutputKind ParseKind(string text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "key" => BindingOutputKind.Key,
            "mousebutton" => BindingOutputKind.MouseButton,
            _ => BindingOutputKind.PadButton
        };

        /// <summary>The output's canonical name for its kind, or null when it is not valid for that kind.</summary>
        public static string CanonicalOutput(BindingOutputKind kind, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string t = name.Trim();
            if (kind == BindingOutputKind.PadButton) t = LegacyIds.ToCanonical(t) ?? t;
            string[] list = kind switch
            {
                BindingOutputKind.PadButton => PaddleMappings.PadButtonTargets,
                BindingOutputKind.Key => InputChoices.Keyboard,
                _ => InputChoices.MouseButtons
            };
            return list.FirstOrDefault(n => string.Equals(n, t, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The valid bindings of a profile. A missing list means none. Unknown inputs drop the binding, unknown outputs drop the output,
        /// out-of-range times are clamped, an output that would press its own input (a feedback loop) is dropped; everything cleared is
        /// reported in <paramref name="warnings"/>.
        /// </summary>
        public static List<BindingDef> Resolve(IEnumerable<BindingEntry> entries, List<string> warnings = null)
        {
            var result = new List<BindingDef>();
            if (entries == null) return result;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BindingEntry e in entries)
            {
                if (e == null) continue;
                if (result.Count >= MaxBindings) { warnings?.Add($"Only the first {MaxBindings} bindings were kept"); break; }
                string input = BindingInputs.Canonical(e.Input);
                if (input == null) { warnings?.Add($"A binding with the unknown input \"{e.Input}\" was removed"); continue; }

                BindingCondition cond = BindingCondition.Held;
                if (!string.IsNullOrWhiteSpace(e.Condition) && !BindingConditions.TryParse(e.Condition, out cond))
                {
                    warnings?.Add($"{InputChoices.DisplayName(input)}: unknown condition \"{e.Condition}\", Held was used");
                    cond = BindingCondition.Held;
                }

                var outputs = new List<BindingOutput>();
                foreach (BindingOutputEntry o in e.Outputs ?? new List<BindingOutputEntry>())
                {
                    if (o == null) continue;
                    if (outputs.Count >= MaxOutputs) { warnings?.Add($"{InputChoices.DisplayName(input)}: only the first {MaxOutputs} outputs were kept"); break; }
                    BindingOutputKind kind = ParseKind(o.Kind);
                    string name = CanonicalOutput(kind, o.Name);
                    if (name == null) { warnings?.Add($"{InputChoices.DisplayName(input)}: unknown output \"{o.Name}\" was removed"); continue; }
                    if (kind != BindingOutputKind.PadButton && string.Equals(name, input, StringComparison.OrdinalIgnoreCase))
                    { warnings?.Add($"{InputChoices.DisplayName(input)}: an output that presses its own input was removed"); continue; }
                    var def = new BindingOutput { Kind = kind, Name = name, Magnitude = Math.Clamp(o.Magnitude, 1, 100) };
                    if (outputs.Any(x => x.Key == def.Key)) continue;
                    outputs.Add(def);
                }

                string id = string.IsNullOrWhiteSpace(e.Id) || !ids.Add(e.Id.Trim()) ? NewId(ids) : e.Id.Trim();
                result.Add(new BindingDef
                {
                    Id = id,
                    Input = input,
                    Enabled = e.Enabled,
                    Condition = cond,
                    Toggle = e.Toggle,
                    DoubleWindowMs = Math.Clamp(e.DoubleWindowMs, MinDoubleMs, MaxDoubleMs),
                    TapMaxMs = Math.Clamp(e.TapMaxMs, MinTapMs, MaxTapMs),
                    HoldMs = Math.Clamp(e.HoldMs, MinHoldMs, MaxHoldMs),
                    Outputs = outputs
                });
            }
            return result;
        }

        private static string NewId(HashSet<string> ids)
        {
            string id;
            do id = Guid.NewGuid().ToString("N"); while (!ids.Add(id));
            return id;
        }

        /// <summary>The file form of the bindings; null when there are none, so profiles that never use them stay exactly as before.</summary>
        public static List<BindingEntry> Build(IEnumerable<BindingDef> defs)
        {
            var list = (defs ?? Array.Empty<BindingDef>()).Select(d => new BindingEntry
            {
                Id = d.Id,
                Input = d.Input,
                Condition = d.Condition.ToString(),
                Toggle = d.Toggle,
                Enabled = d.Enabled,
                DoubleWindowMs = d.DoubleWindowMs,
                TapMaxMs = d.TapMaxMs,
                HoldMs = d.HoldMs,
                Outputs = d.Outputs.Select(o => new BindingOutputEntry { Kind = o.Kind.ToString(), Name = o.Name, Magnitude = o.Magnitude }).ToList()
            }).ToList();
            return list.Count == 0 ? null : list;
        }

        /// <summary>True when two binding lists mean the same thing (ids are ignored; order matters).</summary>
        public static bool Equivalent(IReadOnlyList<BindingDef> a, IReadOnlyList<BindingDef> b)
        {
            a ??= Array.Empty<BindingDef>();
            b ??= Array.Empty<BindingDef>();
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Input != b[i].Input || a[i].Condition != b[i].Condition || a[i].Toggle != b[i].Toggle || a[i].Enabled != b[i].Enabled
                    || a[i].DoubleWindowMs != b[i].DoubleWindowMs || a[i].TapMaxMs != b[i].TapMaxMs || a[i].HoldMs != b[i].HoldMs
                    || a[i].Outputs.Count != b[i].Outputs.Count) return false;
                for (int j = 0; j < a[i].Outputs.Count; j++)
                    if (a[i].Outputs[j].Key != b[i].Outputs[j].Key || a[i].Outputs[j].Magnitude != b[i].Outputs[j].Magnitude) return false;
            }
            return true;
        }

        /// <summary>A warning for an output the active controller cannot do (for example the DualShock touchpad on an Xbox pad), or null.</summary>
        public static string CompatibilityWarning(BindingDef def, ControllerLayoutDef layout)
        {
            if (def == null || layout == null) return null;
            foreach (BindingOutput o in def.Outputs)
                if (o.Kind == BindingOutputKind.PadButton && !layout.Supports(o.Name))
                    return $"{layout.Label(o.Name)} does not exist on the {layout.Name} pad, so this output does nothing.";
            return null;
        }
    }
}
