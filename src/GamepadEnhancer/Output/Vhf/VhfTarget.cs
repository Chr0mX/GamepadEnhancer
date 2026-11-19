using System;
using System.Collections.Generic;

namespace GamepadEnhancer
{
    /// <summary>What the engine needs to create the pad through the VHF driver instead of ViGEmBus.</summary>
    internal sealed class VhfTarget
    {
        private readonly Func<IVhfTransport> transportFactory;

        public VhfTarget(VhfProfileId profile, Func<IVhfTransport> transportFactory = null)
        {
            Profile = profile;
            this.transportFactory = transportFactory;
        }

        public VhfProfileId Profile { get; }

        /// <summary>The real Win32 transport unless a test supplied another one.</summary>
        public IVhfTransport CreateTransport() => transportFactory?.Invoke() ?? new Win32VhfTransport();
    }

    /// <summary>The VHF controller profiles the app offers and how the setting resolves for the selected controller layout.</summary>
    internal static class VhfProfiles
    {
        public const string Auto = "auto", XboxSeries = "xbox_series", XboxOne = "xbox_one", DualShock4 = "dualshock4", DualSense = "dualsense";

        /// <summary>The choices that fit a layout family: an Xbox layout offers the Xbox profiles, a PlayStation layout the PlayStation ones.</summary>
        public static IReadOnlyList<(string Id, string Name)> For(PadFamily family) => family == PadFamily.Xbox360
            ? new[] { (XboxSeries, "Xbox Series"), (XboxOne, "Xbox One") }
            : new[] { (DualShock4, "DualShock 4"), (DualSense, "DualSense") };

        /// <summary>The profile to create: the setting if it fits the layout family, otherwise the family's default (Xbox Series / DualShock 4).</summary>
        public static VhfProfileId Resolve(string setting, PadFamily family)
        {
            bool xbox = family == PadFamily.Xbox360;
            switch ((setting ?? "").Trim().ToLowerInvariant())
            {
                case XboxSeries when xbox: return VhfProfileId.XboxSeries;
                case XboxOne when xbox: return VhfProfileId.XboxOne;
                case DualShock4 when !xbox: return VhfProfileId.DualShock4;
                case DualSense when !xbox: return VhfProfileId.DualSense;
                default: return xbox ? VhfProfileId.XboxSeries : VhfProfileId.DualShock4;
            }
        }

        public static string IdOf(VhfProfileId profile) => profile switch
        {
            VhfProfileId.XboxSeries => XboxSeries,
            VhfProfileId.XboxOne => XboxOne,
            VhfProfileId.DualSense => DualSense,
            _ => DualShock4
        };

        public static string NameOf(VhfProfileId profile) => profile switch
        {
            VhfProfileId.XboxSeries => "Xbox Series",
            VhfProfileId.XboxOne => "Xbox One",
            VhfProfileId.DualSense => "DualSense",
            _ => "DualShock 4"
        };
    }
}
