namespace GamepadEnhancer
{
    /// <summary>
    /// Rules for pushing this PC's mouse buttons to a subscribed Network API client (km.mouse_stream): send on every change,
    /// plus a keep-alive so the client can tell "nothing held" from "link silent". Pure, so it can be unit tested.
    /// </summary>
    internal static class MousePush
    {
        /// <summary>bit0 left, bit1 right, bit2 middle, bit3 X1, bit4 X2.</summary>
        public const int MaskAll = 0x1F;
        public const int PollMs = 4;
        public const long KeepAliveMs = 1000;

        public static bool ShouldSend(bool subscribed, int mask, int lastSent, long nowMs, long lastSendMs) =>
            subscribed && (lastSent < 0 || mask != lastSent || nowMs - lastSendMs >= KeepAliveMs);
    }
}
