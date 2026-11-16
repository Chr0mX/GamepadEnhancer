namespace GamepadEnhancer
{
    /// <summary>Base drift pull direction. Stored in profiles as "Down", "Up", "Left" or "Right"; older profiles used "Center" (= Down).</summary>
    public static class PullDirections
    {
        public static readonly string[] All = { "Down", "Up", "Left", "Right" };

        public static string Normalize(string value) => value switch
        {
            "Up" => "Up",
            "Left" => "Left",
            "Right" => "Right",
            _ => "Down"   // "Down", the old "Center" and anything unknown
        };

        public static bool IsSideways(string value) => Normalize(value) is "Left" or "Right";

        /// <summary>Up / Down pull along Y, so the filtering wiggle goes on X (Left / Right pull along X, wiggle on Y).</summary>
        public static bool IsVertical(string value) => !IsSideways(value);
    }
}
