namespace GamepadEnhancer
{
    /// <summary>
    /// What Apex Legends' default controller preset does on each pad button, shown as a hint beside the binding.
    /// Display only: nothing here changes how input is mapped. Actions that are not on the preset (PS/Guide, touchpad
    /// click, the left-stick keys) have no hint.
    /// </summary>
    internal static class ApexHints
    {
        /// <summary>Ultimate Ability is a combo on the preset, not a button of its own.</summary>
        public const string UltimateNote = "Apex's default controller preset: Ultimate Ability = L1 + R1 together (with the defaults, Q + Mouse 4).";

        public static string For(string action) => action switch
        {
            ControllerEngine.ActionACross => "Jump",
            ControllerEngine.ActionBCircle => "Crouch (toggle)",
            ControllerEngine.ActionXSquare => "Interact / Pickup / Reload  ·  hold: Interact / Pickup",
            ControllerEngine.ActionYTriangle => "Cycle Weapon / Holster  ·  hold: Holster",
            ControllerEngine.ActionLbL1 => "Tactical Ability",
            ControllerEngine.ActionRbR1 => "Ping  ·  hold: Ping Wheel",
            ControllerEngine.ActionLtL2 => "Aim Down Sight",
            ControllerEngine.ActionRtR2 => "Attack",
            ControllerEngine.ActionLsL3 => "Sprint / Toggle Zoom",
            ControllerEngine.ActionRsR3 => "Melee",
            ControllerEngine.ActionDPadUp => "Use Health/Shield Kit  ·  hold: Health Wheel",
            ControllerEngine.ActionDPadRight => "Toggle Fire Mode  ·  hold: Equip Survival Item",
            ControllerEngine.ActionDPadDown => "Equip Grenade  ·  hold: Grenade Wheel",
            ControllerEngine.ActionDPadLeft => "Extra Character Action  ·  hold: Emote Wheel",
            ControllerEngine.ActionBackShare => "Map (toggle)",
            ControllerEngine.ActionStartOptions => "Inventory (toggle)",
            _ => null
        };
    }
}
