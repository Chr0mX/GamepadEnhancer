namespace GamepadEnhancer
{
    /// <summary>What the controller engine accepts from the unified input-state manager.</summary>
    internal interface IInputSink
    {
        void OnKey(string name, bool isDown);
        /// <param name="button">"Mouse Left", "Mouse Right", "Mouse Middle", "Mouse X1" or "Mouse X2".</param>
        void OnMouseButton(string button, bool isDown);
        /// <param name="direction">Positive = wheel up (away from the user), negative = down, 0 = unknown.</param>
        void OnWheel(int direction);
        /// <summary>Relative mouse movement in device counts (from Raw Input).</summary>
        void OnMouseMove(int dx, int dy);
    }
}
