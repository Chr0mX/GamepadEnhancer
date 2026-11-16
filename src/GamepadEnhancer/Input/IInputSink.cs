namespace GamepadEnhancer
{
    /// <summary>What the controller engine accepts from the unified input-state manager.</summary>
    internal interface IInputSink
    {
        void OnKey(string name, bool isDown);
        /// <param name="button">"Mouse Left", "Mouse Right" or "Mouse Middle".</param>
        void OnMouseButton(string button, bool isDown);
        void OnWheel();
    }
}
