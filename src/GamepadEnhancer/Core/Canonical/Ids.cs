using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>
    /// Canonical, controller-independent identifiers for every physical input a virtual controller can have. They describe
    /// <em>position and role</em> (the south face button, the left bumper, the upper-left paddle), never a brand's button name:
    /// whether FACE_SOUTH reads "Cross" or "A" is decided per controller layout (see <see cref="ControllerLayouts"/>). Profiles,
    /// the engine, game catalogs and the diagram all use these ids.
    /// </summary>
    internal static class Inputs
    {
        // Face buttons by compass position (south = bottom).
        public const string FaceSouth = "FACE_SOUTH", FaceEast = "FACE_EAST", FaceWest = "FACE_WEST", FaceNorth = "FACE_NORTH";
        // Shoulders and triggers.
        public const string Lb = "LB", Rb = "RB", Lt = "LT", Rt = "RT";
        // Centre buttons: SELECT is the "back / view / share" button, START the "menu / options" button, HOME the system button.
        public const string Select = "SELECT", Start = "START", Home = "HOME", Touchpad = "TOUCHPAD";
        public const string LsClick = "LS_CLICK", RsClick = "RS_CLICK";
        public const string DpadUp = "DPAD_UP", DpadDown = "DPAD_DOWN", DpadLeft = "DPAD_LEFT", DpadRight = "DPAD_RIGHT";
        // The left stick driven by four keyboard keys (the right stick is mouse movement and has no per-direction input).
        public const string LsUp = "LS_UP", LsDown = "LS_DOWN", LsLeft = "LS_LEFT", LsRight = "LS_RIGHT";
        // Rear paddles.
        public const string PaddleLeftUpper = "PADDLE_LEFT_UPPER", PaddleLeftLower = "PADDLE_LEFT_LOWER",
            PaddleRightUpper = "PADDLE_RIGHT_UPPER", PaddleRightLower = "PADDLE_RIGHT_LOWER";
        /// <summary>The right stick as a whole (mouse movement). Only used as a diagram slot id.</summary>
        public const string RightStickMove = "RS_MOVE";

        /// <summary>Every button-like input the engine can press, in the engine's fixed bit order. Never reorder (saved masks and the Network API depend on it).</summary>
        public static readonly string[] Buttons =
        {
            FaceSouth, FaceEast, FaceWest, FaceNorth,
            Lb, Rb, Lt, Rt,
            Select, Start, LsClick, RsClick,
            DpadUp, DpadDown, DpadLeft, DpadRight,
            Home, Touchpad,
            LsUp, LsDown, LsLeft, LsRight
        };

        public static readonly string[] Paddles = { PaddleLeftUpper, PaddleLeftLower, PaddleRightUpper, PaddleRightLower };

        private static readonly HashSet<string> ButtonSet = new(Buttons, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> PaddleSet = new(Paddles, StringComparer.OrdinalIgnoreCase);

        public static bool IsButton(string id) => id != null && ButtonSet.Contains(id);
        public static bool IsPaddle(string id) => id != null && PaddleSet.Contains(id);
        public static bool IsKnown(string id) => IsButton(id) || IsPaddle(id) || string.Equals(id, RightStickMove, StringComparison.OrdinalIgnoreCase);

        /// <summary>The id in its canonical spelling (upper case), or null when it is not a physical input.</summary>
        public static string Canonical(string id)
        {
            if (id == null) return null;
            foreach (string b in Buttons) if (string.Equals(b, id, StringComparison.OrdinalIgnoreCase)) return b;
            foreach (string p in Paddles) if (string.Equals(p, id, StringComparison.OrdinalIgnoreCase)) return p;
            return string.Equals(id, RightStickMove, StringComparison.OrdinalIgnoreCase) ? RightStickMove : null;
        }

        /// <summary>
        /// A neutral label (no brand names) used when a controller layout has none of its own, for example "Left Bumper" or
        /// "Left Upper Paddle". Unknown ids are prettified ("SOME_ID" becomes "Some Id").
        /// </summary>
        public static string NeutralLabel(string id) => id switch
        {
            FaceSouth => "South Button", FaceEast => "East Button", FaceWest => "West Button", FaceNorth => "North Button",
            Lb => "Left Bumper", Rb => "Right Bumper", Lt => "Left Trigger", Rt => "Right Trigger",
            Select => "Select", Start => "Start", Home => "Home", Touchpad => "Touchpad",
            LsClick => "Left Stick Click", RsClick => "Right Stick Click",
            DpadUp => "D-Pad Up", DpadDown => "D-Pad Down", DpadLeft => "D-Pad Left", DpadRight => "D-Pad Right",
            LsUp => "Left Stick Up", LsDown => "Left Stick Down", LsLeft => "Left Stick Left", LsRight => "Left Stick Right",
            PaddleLeftUpper => "Left Upper Paddle", PaddleLeftLower => "Left Lower Paddle",
            PaddleRightUpper => "Right Upper Paddle", PaddleRightLower => "Right Lower Paddle",
            RightStickMove => "Right Stick",
            _ => Prettify(id)
        };

        private static string Prettify(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            string[] words = id.Trim().ToLowerInvariant().Split('_', ' ');
            return string.Join(" ", words.Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }
    }

    /// <summary>
    /// Identifiers used by profiles written before canonical ids existed ("A / Cross", "LB / L1", "LeftUpper", ...). They mixed two
    /// brands' names into one string, which is exactly what the canonical scheme removes. They are only ever read (and converted);
    /// nothing writes them any more.
    /// </summary>
    internal static class LegacyIds
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A / Cross"] = Inputs.FaceSouth, ["B / Circle"] = Inputs.FaceEast, ["X / Square"] = Inputs.FaceWest, ["Y / Triangle"] = Inputs.FaceNorth,
            ["LB / L1"] = Inputs.Lb, ["RB / R1"] = Inputs.Rb, ["LT / L2"] = Inputs.Lt, ["RT / R2"] = Inputs.Rt,
            ["Back / Share"] = Inputs.Select, ["Start / Options"] = Inputs.Start, ["LS / L3"] = Inputs.LsClick, ["RS / R3"] = Inputs.RsClick,
            ["DPad Up"] = Inputs.DpadUp, ["DPad Down"] = Inputs.DpadDown, ["DPad Left"] = Inputs.DpadLeft, ["DPad Right"] = Inputs.DpadRight,
            ["Guide / PS"] = Inputs.Home, ["Touchpad Click"] = Inputs.Touchpad,
            ["Left Stick Up"] = Inputs.LsUp, ["Left Stick Down"] = Inputs.LsDown, ["Left Stick Left"] = Inputs.LsLeft, ["Left Stick Right"] = Inputs.LsRight,
            ["LeftUpper"] = Inputs.PaddleLeftUpper, ["LeftLower"] = Inputs.PaddleLeftLower,
            ["RightUpper"] = Inputs.PaddleRightUpper, ["RightLower"] = Inputs.PaddleRightLower,
        };

        public static IEnumerable<string> All => Map.Keys;

        /// <summary>The canonical id for a legacy one, or null when <paramref name="legacy"/> is not a legacy id.</summary>
        public static string ToCanonical(string legacy) =>
            legacy != null && Map.TryGetValue(legacy.Trim(), out string canonical) ? canonical : null;
    }

    /// <summary>Normalizes any action reference found in a profile: legacy ids are converted, everything else is upper-cased and trimmed.</summary>
    internal static class ActionIds
    {
        public static string Normalize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            string legacy = LegacyIds.ToCanonical(id);
            if (legacy != null) return legacy;
            return id.Trim().ToUpperInvariant().Replace(' ', '_');
        }
    }
}
