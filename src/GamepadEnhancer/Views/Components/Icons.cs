using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace GamepadEnhancer
{
    /// <summary>
    /// The icon set: 24x24 stroke icons drawn in the Lucide style (2 px stroke, round caps and joins). The paths are entered here as
    /// SVG path data rather than pulling in an icon package, so the app has no extra dependency. A path that fails to parse yields no
    /// icon instead of an exception, so an icon typo can never stop the window from opening.
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dashboard"] = "M3 3h7v9H3z M14 3h7v5h-7z M14 12h7v9h-7z M3 16h7v5H3z",
            ["gamepad"] = "M6 11h4 M8 9v4 M15 12h.01 M18 10h.01 M6 5h12a4 4 0 0 1 4 4l.6 7a3 3 0 0 1-5.2 2.1L15.6 16H8.4l-1.8 2.1A3 3 0 0 1 1.4 16L2 9a4 4 0 0 1 4-4z",
            ["games"] = "M3 3h7v7H3z M14 3h7v7h-7z M14 14h7v7h-7z M3 14h7v7H3z",
            ["paddles"] = "M4 4h5v12a2.5 2.5 0 0 1-5 0z M15 4h5v12a2.5 2.5 0 0 1-5 0z M2 20h20",
            ["mouse"] = "M12 6v4 M5 9a7 7 0 0 1 14 0v6a7 7 0 0 1-14 0z",
            ["profile"] = "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z",
            ["activity"] = "M22 12h-4l-3 9L9 3l-3 9H2",
            ["network"] = "M16 16h6v6h-6z M2 16h6v6H2z M9 2h6v6H9z M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3 M12 12V8",
            ["settings"] = "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4",
            ["folder"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9L9.6 3.9A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2z",
            ["plug"] = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8z",
            ["unplug"] = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8z M3 3l18 18",
            ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
            ["upload"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12",
            ["save"] = "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8",
            ["trash"] = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
            ["restore"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5",
            ["plus"] = "M5 12h14 M12 5v14",
            ["check"] = "M20 6L9 17l-5-5",
            ["x"] = "M18 6L6 18 M6 6l12 12",
            ["copy"] = "M8 8h12v12H8z M4 16V4h12",
            ["key"] = "M21 2l-2 2 M15.4 7.6l3 3L22 7l-3-3 M11.4 11.6a5.5 5.5 0 1 1-7.8 7.8 5.5 5.5 0 0 1 7.8-7.8z M11.4 11.6L15.4 7.6",
            ["shield"] = "M20 13c0 5-3.5 7.5-8 9-4.5-1.5-8-4-8-9V5l8-3 8 3z",
            ["external"] = "M15 3h6v6 M10 14L21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
            ["window"] = "M3 4h18v16H3z M3 9h18",
            ["info"] = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 16v-4 M12 8h.01",
            ["chevron-down"] = "M6 9l6 6 6-6",
            ["chevron-right"] = "M9 6l6 6-6 6",
            ["menu"] = "M4 6h16 M4 12h16 M4 18h16",
            ["send"] = "M22 2L11 13 M22 2l-7 20-4-9-9-4z",
            ["lock"] = "M5 11h14v10H5z M8 11V7a4 4 0 0 1 8 0v4",
            ["eye"] = "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z",
        };

        private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The geometry for an icon name, or null when the name is unknown or its path is invalid.</summary>
        public static Geometry Get(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(kind, out Geometry g)) return g;
                try { g = Paths.TryGetValue(kind, out string data) ? Geometry.Parse(data) : null; }
                catch { g = null; }
                Cache[kind] = g;
                return g;
            }
        }
    }
}
