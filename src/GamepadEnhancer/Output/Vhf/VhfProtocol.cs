// The control-device wire protocol of the libvirtualgamepad VHF driver, transcribed for this client.
//
// Source of the constants and layouts below:
//   https://github.com/Nonary/libvirtualgamepad  tag v0.1.0-beta.11  commit 93dc007a608b341aa998378221f8958f045ee7f2
//   include/libvirtualgamepad/protocol.h
//   Copyright (c) 2026 Chase Payne, MIT License (see THIRD_PARTY_NOTICES.md for the full notice).
//
// Every request is fixed-size, packed, little-endian and METHOD_BUFFERED. The layouts are written and read field by field
// (no struct marshalling) so the offsets are explicit and tested against the upstream static_asserts.

using System;
using System.Buffers.Binary;

namespace GamepadEnhancer
{
    /// <summary>The virtual controller profiles the driver can expose (upstream <c>lvg::profile</c>).</summary>
    internal enum VhfProfileId : ushort
    {
        GenericHid = 1,
        Xbox360 = 2,
        XboxOne = 3,
        XboxSeries = 4,
        DualShock4 = 5,
        DualSense = 6,
        SwitchPro = 7,
        GenericPid = 8,
        SteamController = 9
    }

    /// <summary>The normalized button bits of <c>input_state_request.buttons</c> (upstream <c>lvg::button_mask</c>).</summary>
    internal static class VhfButtons
    {
        public const uint DpadUp = 0x000001, DpadDown = 0x000002, DpadLeft = 0x000004, DpadRight = 0x000008;
        public const uint Start = 0x000010, Back = 0x000020, LeftStick = 0x000040, RightStick = 0x000080;
        public const uint LeftShoulder = 0x000100, RightShoulder = 0x000200, Home = 0x000400;
        public const uint South = 0x001000, East = 0x002000, West = 0x004000, North = 0x008000;
        public const uint Touchpad = 0x100000, Misc = 0x200000;
        public const uint DpadMask = DpadUp | DpadDown | DpadLeft | DpadRight;
    }

    internal enum VhfFeedbackType : ushort
    {
        None = 0,
        GenericRumbleRgb = 1,
        RawOutputReport = 2,
        GenericRumble = 3,
        XboxRumble = 4,
        PlaystationOutput = 5,
        SteamHaptic = 6,
        SteamSettings = 7
    }

    /// <summary>The device-wide answer to <c>ioctl_query_info</c>.</summary>
    internal readonly record struct VhfInfo(ushort MinimumVersion, ushort MaximumVersion, uint AvailableProfiles, uint AvailableFeatures, uint MaximumControllers)
    {
        public bool HasProfile(VhfProfileId profile) => (AvailableProfiles & VhfProtocol.ProfileBit(profile)) != 0;
    }

    /// <summary>One normalized input state. Sticks are signed with positive meaning up and right; triggers are 0..255.</summary>
    internal readonly record struct VhfInput(uint ControllerId, uint Buttons, short LeftX, short LeftY, short RightX, short RightY, byte LeftTrigger, byte RightTrigger);

    /// <summary>Output the host application (a game) wrote to the virtual pad, decoded for display.</summary>
    internal sealed class VhfFeedback
    {
        public uint ControllerId { get; init; }
        public VhfFeedbackType Type { get; init; }
        /// <summary>Low (heavy) and high (light) rumble motors, 0..65535.</summary>
        public ushort LowFrequency { get; init; }
        public ushort HighFrequency { get; init; }
        /// <summary>Xbox trigger rumble motors, 0..65535 (only for <see cref="VhfFeedbackType.XboxRumble"/>).</summary>
        public ushort LeftTrigger { get; init; }
        public ushort RightTrigger { get; init; }
        /// <summary>Lightbar colour (only when <see cref="HasColour"/>).</summary>
        public byte Red { get; init; }
        public byte Green { get; init; }
        public byte Blue { get; init; }
        public bool HasColour { get; init; }
        public byte PlayerLeds { get; init; }
        public bool HasPlayerLeds { get; init; }

        public override string ToString()
        {
            string text = $"rumble {LowFrequency * 100 / 65535}% / {HighFrequency * 100 / 65535}%";
            if (Type == VhfFeedbackType.XboxRumble) text += $", triggers {LeftTrigger * 100 / 65535}% / {RightTrigger * 100 / 65535}%";
            if (HasColour) text += $", light #{Red:X2}{Green:X2}{Blue:X2}";
            if (HasPlayerLeds) text += $", player LEDs {PlayerLeds:X2}";
            return text;
        }
    }

    internal static class VhfProtocol
    {
        // ── Pinned upstream ─────────────────────────────────────────────────────
        public const string UpstreamRepository = "https://github.com/Nonary/libvirtualgamepad";
        public const string UpstreamTag = "v0.1.0-beta.11";
        public const string UpstreamCommit = "93dc007a608b341aa998378221f8958f045ee7f2";

        /// <summary><c>k_protocol_version</c>: a driver that does not accept it is rejected, never reinterpreted.</summary>
        public const ushort Version = 2;
        public const uint MaxControllers = 16;

        /// <summary>The source device's private control interface (never a HID child path).</summary>
        public static readonly Guid InterfaceGuid = new("27debbf5-1d1e-4e9c-906d-d104b1418b2b");
        /// <summary>The hardware id of the root-enumerated source node the setup tool creates.</summary>
        public const string RootHardwareId = @"ROOT\VIBESHINEVIRTUALGAMEPAD";

        // ── Sizes (the static_asserts of protocol.h) ────────────────────────────
        public const int HeaderSize = 8;
        public const int CreateRequestSize = 16;
        public const int QueryInfoRequestSize = 8;
        public const int QueryInfoResponseSize = 28;
        public const int ControllerIdRequestSize = 12;
        public const int InputRequestSize = 28;
        public const int FeedbackEventSize = 48;
        public const int FeedbackPayloadMax = 32;

        // ── IOCTLs: CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800 + n, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA) ──
        private static uint CtlCode(uint function) => (0x22u << 16) | (3u << 14) | (function << 2);
        public static readonly uint IoctlQueryInfo = CtlCode(0x800);
        public static readonly uint IoctlCreateController = CtlCode(0x801);
        public static readonly uint IoctlDestroyController = CtlCode(0x802);
        public static readonly uint IoctlSubmitInputState = CtlCode(0x803);
        public static readonly uint IoctlPollFeedback = CtlCode(0x804);

        /// <summary>The bit of a profile in <c>available_profiles</c> (1 &lt;&lt; (id - 1)).</summary>
        public static uint ProfileBit(VhfProfileId profile)
        {
            int index = (ushort)profile;
            return index == 0 || index > 32 ? 0u : 1u << (index - 1);
        }

        private static void Header(Span<byte> buffer, int size)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)size);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(4), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(6), 0);
        }

        // ── Requests ────────────────────────────────────────────────────────────

        public static byte[] BuildQueryInfo()
        {
            var b = new byte[QueryInfoRequestSize];
            Header(b, b.Length);
            return b;
        }

        public static byte[] BuildCreate(uint controllerId, VhfProfileId profile)
        {
            var b = new byte[CreateRequestSize];
            Header(b, b.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), controllerId);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12), (ushort)profile);
            return b;
        }

        /// <summary>The request of destroy and of poll_feedback (<c>controller_id_request</c>).</summary>
        public static byte[] BuildControllerId(uint controllerId)
        {
            var b = new byte[ControllerIdRequestSize];
            Header(b, b.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), controllerId);
            return b;
        }

        public static byte[] BuildInput(in VhfInput input)
        {
            var b = new byte[InputRequestSize];
            Header(b, b.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), input.ControllerId);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), input.Buttons);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(16), input.LeftX);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(18), input.LeftY);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(20), input.RightX);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(22), input.RightY);
            b[24] = input.LeftTrigger;
            b[25] = input.RightTrigger;
            // bytes 26..27: reserved, must be zero
            return b;
        }

        // ── Responses ───────────────────────────────────────────────────────────

        private static bool ValidHeader(byte[] buffer, int bytes, int size) =>
            buffer != null && bytes == size && buffer.Length >= size
            && BinaryPrimitives.ReadUInt32LittleEndian(buffer) == (uint)size
            && BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)) == Version
            && BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)) == 0;

        /// <summary>
        /// Reads the device-wide query_info answer. Returns <see cref="VhfWin32.ErrorInvalidData"/> for a malformed answer (wrong size,
        /// header or a non-zero counters word, which the device query must keep zero) and
        /// <see cref="VhfWin32.ErrorRevisionMismatch"/> when the driver's supported version range excludes <see cref="Version"/>.
        /// </summary>
        public static int ParseInfo(byte[] buffer, int bytes, out VhfInfo info)
        {
            info = default;
            if (!ValidHeader(buffer, bytes, QueryInfoResponseSize)) return VhfWin32.ErrorInvalidData;
            ushort min = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8));
            ushort max = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10));
            uint profiles = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12));
            uint features = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16));
            uint controllers = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(20));
            uint counters = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(24));
            if (controllers == 0 || controllers > MaxControllers || counters != 0) return VhfWin32.ErrorInvalidData;
            if (min > Version || max < Version) return VhfWin32.ErrorRevisionMismatch;
            info = new VhfInfo(min, max, profiles, features, controllers);
            return VhfWin32.ErrorSuccess;
        }

        /// <summary>The payload size the driver must send for a feedback type, or null for an unknown type.</summary>
        public static int? FeedbackPayloadSize(VhfFeedbackType type) => type switch
        {
            VhfFeedbackType.None => 0,
            VhfFeedbackType.GenericRumbleRgb or VhfFeedbackType.GenericRumble => 8,
            VhfFeedbackType.RawOutputReport => 16,
            VhfFeedbackType.XboxRumble => 8,
            VhfFeedbackType.PlaystationOutput => 32,
            VhfFeedbackType.SteamHaptic => 16,
            VhfFeedbackType.SteamSettings => 32,
            _ => null
        };

        /// <summary>
        /// Validates and decodes one feedback event exactly as upstream's <c>poll_feedback</c> does: the whole 48 bytes, a valid header,
        /// the right controller, and a payload size that is the full struct of its type. Anything else is ERROR_INVALID_DATA.
        /// </summary>
        public static int ParseFeedback(byte[] buffer, int bytes, uint expectedController, out VhfFeedback feedback)
        {
            feedback = null;
            if (!ValidHeader(buffer, bytes, FeedbackEventSize)) return VhfWin32.ErrorInvalidData;
            uint controller = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
            var type = (VhfFeedbackType)BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(12));
            int payloadSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14));
            int? expected = FeedbackPayloadSize(type);
            if (controller != expectedController || payloadSize > FeedbackPayloadMax || expected == null || payloadSize != expected.Value)
                return VhfWin32.ErrorInvalidData;

            ReadOnlySpan<byte> p = buffer.AsSpan(16, FeedbackPayloadMax);
            switch (type)
            {
                case VhfFeedbackType.GenericRumble:
                    feedback = new VhfFeedback { ControllerId = controller, Type = type, LowFrequency = U16(p, 0), HighFrequency = U16(p, 2) };
                    break;
                case VhfFeedbackType.GenericRumbleRgb:
                    feedback = new VhfFeedback
                    {
                        ControllerId = controller, Type = type, LowFrequency = U16(p, 0), HighFrequency = U16(p, 2),
                        Red = p[4], Green = p[5], Blue = p[6], HasColour = true
                    };
                    break;
                case VhfFeedbackType.XboxRumble:
                    feedback = new VhfFeedback
                    {
                        ControllerId = controller, Type = type, LowFrequency = U16(p, 0), HighFrequency = U16(p, 2),
                        LeftTrigger = U16(p, 4), RightTrigger = U16(p, 6)
                    };
                    break;
                case VhfFeedbackType.PlaystationOutput:
                {
                    byte valid = p[7];   // ps_output_lightbar_valid 1, triggers 2, player LEDs 4, microphone LED 8
                    feedback = new VhfFeedback
                    {
                        ControllerId = controller, Type = type, LowFrequency = U16(p, 0), HighFrequency = U16(p, 2),
                        Red = p[4], Green = p[5], Blue = p[6], HasColour = (valid & 0x01) != 0,
                        PlayerLeds = p[8], HasPlayerLeds = (valid & 0x04) != 0
                    };
                    break;
                }
                default:
                    // a Steam Controller or raw HID report: valid, but nothing this app shows
                    feedback = new VhfFeedback { ControllerId = controller, Type = type };
                    break;
            }
            return VhfWin32.ErrorSuccess;
        }

        private static ushort U16(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
    }

    /// <summary>The Win32 error codes the driver's contract uses (client.h documents which call returns which).</summary>
    internal static class VhfWin32
    {
        public const int ErrorSuccess = 0;
        public const int ErrorFileNotFound = 2;
        public const int ErrorAccessDenied = 5;
        public const int ErrorInvalidHandle = 6;
        public const int ErrorNotEnoughMemory = 8;
        public const int ErrorInvalidData = 13;
        public const int ErrorNotReady = 21;
        public const int ErrorNotSupported = 50;
        public const int ErrorInvalidParameter = 87;
        public const int ErrorBusy = 170;
        public const int ErrorNoMoreItems = 259;
        public const int ErrorRevisionMismatch = 1306;
        public const int ErrorNotFound = 1168;
        public const int ErrorDeviceRemoved = 1617;
        public const int ErrorInvalidUserBuffer = 1784;
    }
}
