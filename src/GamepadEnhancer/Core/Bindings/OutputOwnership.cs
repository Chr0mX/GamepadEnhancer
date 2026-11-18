using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>
    /// Who is holding which output. An output (a pad button, a key, a mouse button) is down while at least one binding owns it, and is
    /// released only when its last owner lets go, so one binding stopping can never release something another binding still holds.
    /// For the pad triggers the level is the largest magnitude among the owners. Not thread safe: the runtime locks around it.
    /// </summary>
    internal sealed class OutputOwnership
    {
        private readonly Dictionary<string, Dictionary<string, int>> owners = new(StringComparer.Ordinal);

        /// <summary>The level of an output: 0 when nobody holds it, otherwise the largest magnitude (1..100) among its owners.</summary>
        public int Level(string output) =>
            owners.TryGetValue(output, out var map) && map.Count > 0 ? map.Values.Max() : 0;

        public bool IsHeld(string output) => Level(output) > 0;

        public IEnumerable<string> HeldOutputs => owners.Where(p => p.Value.Count > 0).Select(p => p.Key).ToList();

        /// <summary>Makes <paramref name="owner"/> hold the output. Returns true when the output's level changed.</summary>
        public bool Claim(string owner, string output, int magnitude = 100)
        {
            int before = Level(output);
            if (!owners.TryGetValue(output, out var map)) owners[output] = map = new Dictionary<string, int>(StringComparer.Ordinal);
            map[owner] = Math.Clamp(magnitude, 1, 100);
            return Level(output) != before;
        }

        /// <summary>Lets go of one output. Returns true when the output's level changed (the last owner left or the maximum dropped).</summary>
        public bool Release(string owner, string output)
        {
            if (!owners.TryGetValue(output, out var map) || !map.ContainsKey(owner)) return false;
            int before = Level(output);
            map.Remove(owner);
            if (map.Count == 0) owners.Remove(output);
            return Level(output) != before;
        }

        /// <summary>Lets go of everything <paramref name="owner"/> holds; returns the outputs whose level changed.</summary>
        public List<string> ReleaseAll(string owner)
        {
            var changed = new List<string>();
            foreach (string output in owners.Where(p => p.Value.ContainsKey(owner)).Select(p => p.Key).ToList())
                if (Release(owner, output)) changed.Add(output);
            return changed;
        }

        /// <summary>Drops every claim (disconnect, shutdown); returns every output that was held.</summary>
        public List<string> ReleaseEverything()
        {
            var held = HeldOutputs.ToList();
            owners.Clear();
            return held;
        }
    }
}
