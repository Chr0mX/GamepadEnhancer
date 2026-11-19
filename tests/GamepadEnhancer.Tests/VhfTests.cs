using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Xunit;

namespace GamepadEnhancer.Tests
{
    /// <summary>A stand-in for the driver: answers the control IOCTLs and records every request so the real wire bytes can be checked.</summary>
    internal sealed class FakeVhfTransport : IVhfTransport
    {
        public bool IsOpen { get; private set; }
        public int OpenResult;
        public readonly List<(uint Code, byte[] Input)> Calls = new();
        public uint Profiles = VhfProtocol.ProfileBit(VhfProfileId.XboxOne) | VhfProtocol.ProfileBit(VhfProfileId.XboxSeries)
                               | VhfProtocol.ProfileBit(VhfProfileId.DualShock4) | VhfProtocol.ProfileBit(VhfProfileId.DualSense);
        public ushort MinVersion = VhfProtocol.Version, MaxVersion = VhfProtocol.Version;
        public int QueryResult, CreateResult, DestroyResult, SubmitResult;
        public int BusyCreates;                                  // this many creates answer ERROR_BUSY first
        public readonly Queue<byte[]> Feedback = new();
        public int CloseCount;

        public int Open() { if (OpenResult != 0) return OpenResult; IsOpen = true; return 0; }
        public void Close() { IsOpen = false; CloseCount++; }
        public void Dispose() => Close();

        public int Ioctl(uint code, byte[] input, byte[] output, out int bytesReturned)
        {
            bytesReturned = 0;
            Calls.Add((code, input));
            if (code == VhfProtocol.IoctlQueryInfo)
            {
                if (QueryResult != 0) return QueryResult;
                byte[] info = InfoResponse(MinVersion, MaxVersion, Profiles);
                info.CopyTo(output, 0);
                bytesReturned = info.Length;
                return 0;
            }
            if (code == VhfProtocol.IoctlCreateController)
            {
                if (BusyCreates > 0) { BusyCreates--; return VhfWin32.ErrorBusy; }
                return CreateResult;
            }
            if (code == VhfProtocol.IoctlDestroyController) return DestroyResult;
            if (code == VhfProtocol.IoctlSubmitInputState) return SubmitResult;
            if (code == VhfProtocol.IoctlPollFeedback)
            {
                if (Feedback.Count == 0) return VhfWin32.ErrorNoMoreItems;
                byte[] fb = Feedback.Dequeue();
                fb.CopyTo(output, 0);
                bytesReturned = fb.Length;
                return 0;
            }
            return VhfWin32.ErrorInvalidParameter;
        }

        public int Count(uint code) => Calls.Count(c => c.Code == code);
        public byte[] LastInput() => Calls.Last(c => c.Code == VhfProtocol.IoctlSubmitInputState).Input;

        public static byte[] InfoResponse(ushort min, ushort max, uint profiles, uint controllers = 16, uint counters = 0)
        {
            var b = new byte[VhfProtocol.QueryInfoResponseSize];
            BinaryPrimitives.WriteUInt32LittleEndian(b, 28);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), VhfProtocol.Version);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), min);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), max);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), profiles);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 0x3F);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), controllers);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), counters);
            return b;
        }

        /// <summary>A 48-byte feedback_event with the payload bytes given.</summary>
        public static byte[] FeedbackEvent(uint controller, ushort type, ushort payloadSize, params byte[] payload)
        {
            var b = new byte[VhfProtocol.FeedbackEventSize];
            BinaryPrimitives.WriteUInt32LittleEndian(b, 48);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), VhfProtocol.Version);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), controller);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12), type);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), payloadSize);
            payload.CopyTo(b, 16);
            return b;
        }

        public static byte[] XboxRumble(uint controller, ushort low, ushort high, ushort lt, ushort rt)
        {
            var p = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(p, low);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), high);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), lt);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), rt);
            return FeedbackEvent(controller, (ushort)VhfFeedbackType.XboxRumble, 8, p);
        }
    }

    public class VhfProtocolTests
    {
        [Fact]
        public void PinnedUpstreamIsTheDocumentedOne()
        {
            Assert.Equal("v0.1.0-beta.11", VhfProtocol.UpstreamTag);
            Assert.Equal("93dc007a608b341aa998378221f8958f045ee7f2", VhfProtocol.UpstreamCommit);
            Assert.Equal(2, VhfProtocol.Version);
        }

        [Fact]
        public void RequestSizesMatchTheUpstreamStaticAsserts()
        {
            Assert.Equal(8, VhfProtocol.BuildQueryInfo().Length);
            Assert.Equal(16, VhfProtocol.BuildCreate(0, VhfProfileId.XboxSeries).Length);
            Assert.Equal(12, VhfProtocol.BuildControllerId(0).Length);
            Assert.Equal(28, VhfProtocol.BuildInput(default).Length);
            Assert.Equal(28, VhfProtocol.QueryInfoResponseSize);
            Assert.Equal(48, VhfProtocol.FeedbackEventSize);
        }

        [Fact]
        public void HeaderIsSizeVersionReserved()
        {
            byte[] q = VhfProtocol.BuildQueryInfo();
            Assert.Equal(new byte[] { 8, 0, 0, 0, 2, 0, 0, 0 }, q);
        }

        [Fact]
        public void CreateRequestBytes()
        {
            byte[] b = VhfProtocol.BuildCreate(3, VhfProfileId.XboxSeries);
            Assert.Equal(new byte[] { 16, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0 }, b);
        }

        [Fact]
        public void ControllerIdRequestBytes()
        {
            Assert.Equal(new byte[] { 12, 0, 0, 0, 2, 0, 0, 0, 5, 0, 0, 0 }, VhfProtocol.BuildControllerId(5));
        }

        [Fact]
        public void InputRequestBytesAreLittleEndianAtTheUpstreamOffsets()
        {
            var input = new VhfInput(1, VhfButtons.South | VhfButtons.DpadUp, -2, 0x1234, short.MaxValue, short.MinValue, 0x80, 0xFF);
            byte[] b = VhfProtocol.BuildInput(input);
            Assert.Equal(new byte[]
            {
                28, 0, 0, 0, 2, 0, 0, 0,          // header
                1, 0, 0, 0,                       // controller id
                0x01, 0x10, 0, 0,                 // buttons: DpadUp | South
                0xFE, 0xFF, 0x34, 0x12,           // left x, left y
                0xFF, 0x7F, 0x00, 0x80,           // right x, right y
                0x80, 0xFF,                       // triggers
                0, 0                              // reserved
            }, b);
        }

        [Fact]
        public void IoctlCodesAreCtlCodeUnknownBufferedReadWrite()
        {
            Assert.Equal(0x0022E000u, VhfProtocol.IoctlQueryInfo);
            Assert.Equal(0x0022E004u, VhfProtocol.IoctlCreateController);
            Assert.Equal(0x0022E008u, VhfProtocol.IoctlDestroyController);
            Assert.Equal(0x0022E00Cu, VhfProtocol.IoctlSubmitInputState);
            Assert.Equal(0x0022E010u, VhfProtocol.IoctlPollFeedback);
        }

        [Fact]
        public void InterfaceGuidIsUpstreams()
        {
            var expected = new Guid(0x27debbf5, 0x1d1e, 0x4e9c, 0x90, 0x6d, 0xd1, 0x04, 0xb1, 0x41, 0x8b, 0x2b);
            Assert.Equal(expected, VhfProtocol.InterfaceGuid);
        }

        [Fact]
        public void ProfileBitsAreOneShiftedByIdMinusOne()
        {
            Assert.Equal(1u, VhfProtocol.ProfileBit(VhfProfileId.GenericHid));
            Assert.Equal(8u, VhfProtocol.ProfileBit(VhfProfileId.XboxSeries));
            Assert.Equal(32u, VhfProtocol.ProfileBit(VhfProfileId.DualSense));
            Assert.Equal(0u, VhfProtocol.ProfileBit((VhfProfileId)0));
        }

        [Fact]
        public void ButtonBitsAreUpstreams()
        {
            Assert.Equal(0x1000u, VhfButtons.South);
            Assert.Equal(0x8000u, VhfButtons.North);
            Assert.Equal(0x100000u, VhfButtons.Touchpad);
            Assert.Equal(0xFu, VhfButtons.DpadMask);
        }

        [Fact]
        public void InfoIsParsedAndValidated()
        {
            byte[] ok = FakeVhfTransport.InfoResponse(2, 2, 0x3C);
            Assert.Equal(0, VhfProtocol.ParseInfo(ok, ok.Length, out VhfInfo info));
            Assert.Equal(16u, info.MaximumControllers);
            Assert.True(info.HasProfile(VhfProfileId.XboxSeries));
            Assert.False(info.HasProfile(VhfProfileId.SwitchPro));

            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseInfo(ok, ok.Length - 1, out _));                          // wrong size
            Assert.Equal(VhfWin32.ErrorRevisionMismatch, VhfProtocol.ParseInfo(FakeVhfTransport.InfoResponse(2, 1, 0x3C), 28, out _));   // max < ours
            Assert.Equal(VhfWin32.ErrorRevisionMismatch, VhfProtocol.ParseInfo(FakeVhfTransport.InfoResponse(3, 4, 0x3C), 28, out _));   // min > ours
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseInfo(FakeVhfTransport.InfoResponse(2, 2, 0x3C, controllers: 0), 28, out _));
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseInfo(FakeVhfTransport.InfoResponse(2, 2, 0x3C, controllers: 17), 28, out _));
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseInfo(FakeVhfTransport.InfoResponse(2, 2, 0x3C, counters: 1), 28, out _));

            byte[] badHeader = (byte[])ok.Clone();
            badHeader[4] = 1;   // header version
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseInfo(badHeader, badHeader.Length, out _));
        }

        [Fact]
        public void XboxRumbleFeedbackIsDecoded()
        {
            byte[] e = FakeVhfTransport.XboxRumble(0, 65535, 32768, 100, 0);
            Assert.Equal(0, VhfProtocol.ParseFeedback(e, e.Length, 0, out VhfFeedback fb));
            Assert.Equal(VhfFeedbackType.XboxRumble, fb.Type);
            Assert.Equal(65535, fb.LowFrequency);
            Assert.Equal(32768, fb.HighFrequency);
            Assert.Equal(100, fb.LeftTrigger);
            Assert.False(fb.HasColour);
            Assert.Contains("rumble 100% / 50%", fb.ToString());
        }

        [Fact]
        public void PlaystationFeedbackCarriesRumbleLightbarAndLedsOnlyWhenValid()
        {
            var p = new byte[32];
            BinaryPrimitives.WriteUInt16LittleEndian(p, 1000);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), 2000);
            p[4] = 10; p[5] = 20; p[6] = 30;
            p[7] = 0x01 | 0x04;      // lightbar and player LEDs valid
            p[8] = 0x15;
            byte[] e = FakeVhfTransport.FeedbackEvent(2, (ushort)VhfFeedbackType.PlaystationOutput, 32, p);
            Assert.Equal(0, VhfProtocol.ParseFeedback(e, e.Length, 2, out VhfFeedback fb));
            Assert.True(fb.HasColour);
            Assert.Equal(20, fb.Green);
            Assert.True(fb.HasPlayerLeds);
            Assert.Equal(0x15, fb.PlayerLeds);

            p[7] = 0;                // nothing valid: the colour is not meaningful
            e = FakeVhfTransport.FeedbackEvent(2, (ushort)VhfFeedbackType.PlaystationOutput, 32, p);
            Assert.Equal(0, VhfProtocol.ParseFeedback(e, e.Length, 2, out fb));
            Assert.False(fb.HasColour);
            Assert.False(fb.HasPlayerLeds);
        }

        [Fact]
        public void MalformedFeedbackIsRejectedLikeUpstream()
        {
            byte[] good = FakeVhfTransport.XboxRumble(1, 1, 1, 1, 1);
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseFeedback(good, good.Length, 0, out _));          // another controller
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseFeedback(good, good.Length - 1, 1, out _));      // short
            byte[] wrongSize = FakeVhfTransport.FeedbackEvent(1, (ushort)VhfFeedbackType.XboxRumble, 7, new byte[8]);
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseFeedback(wrongSize, wrongSize.Length, 1, out _)); // size is not the type's
            byte[] unknown = FakeVhfTransport.FeedbackEvent(1, 99, 0);
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseFeedback(unknown, unknown.Length, 1, out _));    // unknown type
            byte[] tooBig = FakeVhfTransport.FeedbackEvent(1, (ushort)VhfFeedbackType.XboxRumble, 33, new byte[8]);
            Assert.Equal(VhfWin32.ErrorInvalidData, VhfProtocol.ParseFeedback(tooBig, tooBig.Length, 1, out _));      // larger than the payload area
        }

        [Fact]
        public void RealTransportReportsAMissingDriverWithoutThrowing()
        {
            // On a machine without the driver this is ERROR_NOT_FOUND; with it installed the handle really opens. Either way: no exception.
            using var transport = new Win32VhfTransport();
            int code = transport.Open();
            Assert.Equal(code == 0, transport.IsOpen);
            if (code != 0) Assert.Equal(VhfWin32.ErrorNotFound, code);
        }
    }

    public class VhfClientTests
    {
        private static (VhfClient Client, FakeVhfTransport Transport) Make()
        {
            var t = new FakeVhfTransport();
            return (new VhfClient(t), t);
        }

        [Fact]
        public void ConnectDoesTheVersionHandshake()
        {
            var (c, t) = Make();
            Assert.Equal(0, c.Connect());
            Assert.True(c.Connected);
            Assert.Equal(1, t.Count(VhfProtocol.IoctlQueryInfo));
            Assert.Equal(new byte[] { 8, 0, 0, 0, 2, 0, 0, 0 }, t.Calls[0].Input);
            Assert.Equal(16u, c.Info.MaximumControllers);
        }

        [Fact]
        public void ConnectFailsWithNotFoundWhenTheDriverIsMissing()
        {
            var (c, t) = Make();
            t.OpenResult = VhfWin32.ErrorNotFound;
            Assert.Equal(VhfWin32.ErrorNotFound, c.Connect());
            Assert.False(c.Connected);
            Assert.Contains("not installed", VhfErrors.Describe(VhfWin32.ErrorNotFound));
        }

        [Fact]
        public void AnOlderOrNewerDriverIsRefusedAsARevisionMismatchAndTheHandleClosed()
        {
            var (c, t) = Make();
            t.MaxVersion = 1;
            Assert.Equal(VhfWin32.ErrorRevisionMismatch, c.Connect());
            Assert.False(c.Connected);
            Assert.False(t.IsOpen);
        }

        [Fact]
        public void InvalidUserBufferIsReportedAsARevisionMismatchOnEveryIoctl()
        {
            var (c, t) = Make();
            Assert.Equal(0, c.Connect());
            t.SubmitResult = VhfWin32.ErrorInvalidUserBuffer;
            Assert.Equal(VhfWin32.ErrorRevisionMismatch, c.SubmitInput(new VhfInput(0, 0, 0, 0, 0, 0, 0, 0)));
        }

        [Fact]
        public void CreateChecksTheSlotAndTheProfileBeforeAskingTheDriver()
        {
            var (c, t) = Make();
            Assert.Equal(VhfWin32.ErrorInvalidHandle, c.CreateController(0, VhfProfileId.XboxSeries));   // not connected
            Assert.Equal(0, c.Connect());
            Assert.Equal(VhfWin32.ErrorInvalidParameter, c.CreateController(16, VhfProfileId.XboxSeries));
            Assert.Equal(VhfWin32.ErrorNotSupported, c.CreateController(0, VhfProfileId.Xbox360));       // VHF cannot make an Xbox 360
            Assert.Equal(VhfWin32.ErrorNotSupported, c.CreateController(0, VhfProfileId.GenericHid));    // disabled upstream
            Assert.Equal(0, t.Count(VhfProtocol.IoctlCreateController));
            Assert.Equal(0, c.CreateController(0, VhfProfileId.DualSense));
            Assert.Equal(1, t.Count(VhfProtocol.IoctlCreateController));
        }

        [Fact]
        public void PollFeedbackReturnsNoMoreItemsWhenQuietAndDecodesWhenNot()
        {
            var (c, t) = Make();
            Assert.Equal(0, c.Connect());
            Assert.Equal(VhfWin32.ErrorNoMoreItems, c.PollFeedback(0, out _));
            t.Feedback.Enqueue(FakeVhfTransport.XboxRumble(0, 5, 6, 7, 8));
            Assert.Equal(0, c.PollFeedback(0, out VhfFeedback fb));
            Assert.Equal(5, fb.LowFrequency);
            t.Feedback.Enqueue(FakeVhfTransport.XboxRumble(3, 5, 6, 7, 8));
            Assert.Equal(VhfWin32.ErrorInvalidData, c.PollFeedback(0, out _));
        }

        [Fact]
        public void CloseReleasesTheHandleSoTheDriverFreesTheSlots()
        {
            var (c, t) = Make();
            Assert.Equal(0, c.Connect());
            c.Close();
            Assert.False(c.Connected);
            Assert.False(t.IsOpen);
            Assert.Equal(VhfWin32.ErrorInvalidHandle, c.SubmitInput(default));
        }
    }

    public class VhfPadTests
    {
        private sealed class Harness
        {
            public readonly FakeVhfTransport Transport = new();
            public readonly VhfClient Client;
            public long Now;
            public int Sleeps;
            public readonly List<string> Status = new();
            public readonly List<VhfFeedback> Feedback = new();
            public VhfPad Pad;

            public Harness(VhfProfileId profile)
            {
                Client = new VhfClient(Transport);
                Pad = new VhfPad(Client, profile, 0, () => Now, _ => Sleeps++);
                Pad.StatusChanged += s => Status.Add(s);
                Pad.FeedbackReceived += f => Feedback.Add(f);
            }

            public VhfInput Parse()
            {
                byte[] b = Transport.LastInput();
                return new VhfInput(
                    BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12)),
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(16)), BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(18)),
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(20)), BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(22)), b[24], b[25]);
            }
        }

        [Fact]
        public void XboxButtonsAxesAndTriggersAreFormattedForTheDriver()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Pad.SetButtonState(Xbox360Button.A, true);
            h.Pad.SetButtonState(Xbox360Button.LeftShoulder, true);
            h.Pad.SetButtonState(Xbox360Button.Up, true);
            h.Pad.SetButtonState(Xbox360Button.Guide, true);
            h.Pad.SetAxisValue(Xbox360Axis.LeftThumbX, 1000);
            h.Pad.SetAxisValue(Xbox360Axis.LeftThumbY, -2000);
            h.Pad.SetAxisValue(Xbox360Axis.RightThumbX, short.MaxValue);
            h.Pad.SetAxisValue(Xbox360Axis.RightThumbY, short.MinValue);
            h.Pad.SetSliderValue(Xbox360Slider.LeftTrigger, 200);
            h.Pad.SetSliderValue(Xbox360Slider.RightTrigger, 255);
            h.Pad.SubmitReport();

            VhfInput sent = h.Parse();
            Assert.Equal(VhfButtons.South | VhfButtons.LeftShoulder | VhfButtons.DpadUp | VhfButtons.Home, sent.Buttons);
            Assert.Equal(1000, sent.LeftX);
            Assert.Equal(-2000, sent.LeftY);          // Xbox +Y is already "up": no inversion
            Assert.Equal(short.MaxValue, sent.RightX);
            Assert.Equal(short.MinValue, sent.RightY);
            Assert.Equal(200, sent.LeftTrigger);
            Assert.Equal(255, sent.RightTrigger);
        }

        [Fact]
        public void EveryXboxButtonHasItsOwnBit()
        {
            var seen = new HashSet<uint>();
            foreach (var button in new[] { Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y, Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder,
                Xbox360Button.Back, Xbox360Button.Start, Xbox360Button.LeftThumb, Xbox360Button.RightThumb, Xbox360Button.Guide,
                Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right })
            {
                var h = new Harness(VhfProfileId.XboxSeries);
                h.Pad.SetButtonState(button, true);
                uint bits = h.Pad.CurrentState.Buttons;
                Assert.NotEqual(0u, bits);
                Assert.True(seen.Add(bits), "two buttons share a bit");
                h.Pad.SetButtonState(button, false);
                Assert.Equal(0u, h.Pad.CurrentState.Buttons);
            }
            Assert.Equal(15, seen.Count);
        }

        [Fact]
        public void DualShock4AxesAreConvertedToSignedPositiveUpRight()
        {
            var h = new Harness(VhfProfileId.DualShock4);
            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbY, 128);
            Assert.Equal(0, h.Pad.CurrentState.LeftX);
            Assert.Equal(0, h.Pad.CurrentState.LeftY);

            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbX, 255);   // full right
            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbY, 0);     // DS4 Y counts down: 0 is full up
            Assert.Equal(32639, h.Pad.CurrentState.LeftX);
            Assert.Equal(short.MaxValue, h.Pad.CurrentState.LeftY);   // 32896 clamps

            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbX, 0);     // full left
            h.Pad.SetAxisValue(DualShock4Axis.LeftThumbY, 255);   // full down
            Assert.Equal(short.MinValue, h.Pad.CurrentState.LeftX);   // -32896 clamps
            Assert.Equal(-32639, h.Pad.CurrentState.LeftY);

            h.Pad.SetAxisValue(DualShock4Axis.RightThumbX, 255);
            h.Pad.SetAxisValue(DualShock4Axis.RightThumbY, 255);
            Assert.Equal(32639, h.Pad.CurrentState.RightX);
            Assert.Equal(-32639, h.Pad.CurrentState.RightY);
        }

        [Fact]
        public void DualShock4ButtonsTouchpadPsAndDpadAreMapped()
        {
            var h = new Harness(VhfProfileId.DualSense);
            h.Pad.SetButtonState(DualShock4Button.Cross, true);
            h.Pad.SetButtonState(DualShock4Button.Share, true);
            h.Pad.SetButtonState(DualShock4Button.Options, true);
            h.Pad.SetButtonState(DualShock4SpecialButton.Ps, true);
            h.Pad.SetButtonState(DualShock4SpecialButton.Touchpad, true);
            h.Pad.SetButtonState(DualShock4Button.TriggerLeft, true);          // analog trigger carries it: no bit
            Assert.Equal(VhfButtons.South | VhfButtons.Back | VhfButtons.Start | VhfButtons.Home | VhfButtons.Touchpad, h.Pad.CurrentState.Buttons);

            h.Pad.SetDPadDirection(DualShock4DPadDirection.Northeast);
            Assert.Equal(VhfButtons.DpadUp | VhfButtons.DpadRight, h.Pad.CurrentState.Buttons & VhfButtons.DpadMask);
            h.Pad.SetDPadDirection(DualShock4DPadDirection.South);
            Assert.Equal(VhfButtons.DpadDown, h.Pad.CurrentState.Buttons & VhfButtons.DpadMask);
            h.Pad.SetDPadDirection(DualShock4DPadDirection.None);
            Assert.Equal(0u, h.Pad.CurrentState.Buttons & VhfButtons.DpadMask);
            Assert.NotEqual(0u, h.Pad.CurrentState.Buttons & VhfButtons.South);   // the face buttons are untouched by the hat
        }

        [Fact]
        public void AnUnchangedStateIsResentOnlyAsAHeartbeat()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Pad.SubmitReport();
            Assert.Equal(1, h.Transport.Count(VhfProtocol.IoctlSubmitInputState));
            h.Now = 50;
            h.Pad.SubmitReport();
            Assert.Equal(1, h.Transport.Count(VhfProtocol.IoctlSubmitInputState));    // unchanged, not due
            h.Pad.SetButtonState(Xbox360Button.B, true);
            h.Pad.SubmitReport();
            Assert.Equal(2, h.Transport.Count(VhfProtocol.IoctlSubmitInputState));    // changed: sent at once
            h.Now = 160;
            h.Pad.SubmitReport();
            Assert.Equal(3, h.Transport.Count(VhfProtocol.IoctlSubmitInputState));    // 100 ms heartbeat
        }

        [Fact]
        public void ConnectCreatesTheSlotWithTheChosenProfileAndDisconnectRemovesIt()
        {
            var h = new Harness(VhfProfileId.DualSense);
            h.Pad.Connect();
            byte[] create = h.Transport.Calls.Single(c => c.Code == VhfProtocol.IoctlCreateController).Input;
            Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(create.AsSpan(12)));
            h.Pad.Disconnect();
            Assert.Equal(1, h.Transport.Count(VhfProtocol.IoctlDestroyController));
            Assert.False(h.Transport.IsOpen);
            h.Pad.SubmitReport();   // after Disconnect nothing is sent
            Assert.Equal(0, h.Transport.Count(VhfProtocol.IoctlSubmitInputState));
        }

        [Fact]
        public void BusyCreateIsRetriedAndThenGivesUpWithAClearMessage()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Transport.BusyCreates = 2;
            h.Pad.Connect();
            Assert.Equal(2, h.Sleeps);

            var stuck = new Harness(VhfProfileId.XboxSeries);
            stuck.Transport.BusyCreates = 100;
            var ex = Assert.Throws<VhfException>(() => stuck.Pad.Connect());
            Assert.Equal(VhfWin32.ErrorBusy, ex.Win32Error);
            Assert.Equal(6, stuck.Transport.Count(VhfProtocol.IoctlCreateController));
            Assert.False(stuck.Transport.IsOpen);   // the handle is released again
        }

        [Fact]
        public void MissingDriverAndMissingProfileGiveSpecificMessages()
        {
            var none = new Harness(VhfProfileId.XboxSeries);
            none.Transport.OpenResult = VhfWin32.ErrorNotFound;
            var missing = Assert.Throws<VhfException>(() => none.Pad.Connect());
            Assert.Contains("not installed", missing.Message);

            var limited = new Harness(VhfProfileId.DualSense);
            limited.Transport.Profiles = VhfProtocol.ProfileBit(VhfProfileId.XboxSeries);
            var noProfile = Assert.Throws<VhfException>(() => limited.Pad.Connect());
            Assert.Equal(VhfWin32.ErrorNotSupported, noProfile.Win32Error);
            Assert.Contains("DualSense", noProfile.Message);

            var mismatch = new Harness(VhfProfileId.XboxSeries);
            mismatch.Transport.MaxVersion = 1;
            Assert.Contains("protocol", Assert.Throws<VhfException>(() => mismatch.Pad.Connect()).Message);
        }

        [Fact]
        public void ALostControllerIsRecreatedAfterResumeAndTheStateIsResent()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Pad.SetButtonState(Xbox360Button.A, true);
            h.Pad.SubmitReport();

            h.Transport.SubmitResult = VhfWin32.ErrorDeviceRemoved;   // sleep/resume removed the child
            h.Now = 150;                                               // past the heartbeat, so a report is really sent
            h.Pad.SubmitReport();
            Assert.True(h.Pad.ControllerLost);
            Assert.Contains(h.Status, s => s.Contains("lost"));

            h.Transport.SubmitResult = 0;
            int creates = h.Transport.Count(VhfProtocol.IoctlCreateController);
            h.Now = 160;
            h.Pad.SubmitReport();                                      // the first recovery attempt is immediate
            Assert.False(h.Pad.ControllerLost);
            Assert.Equal(creates + 1, h.Transport.Count(VhfProtocol.IoctlCreateController));
            Assert.Equal(1, h.Transport.Count(VhfProtocol.IoctlDestroyController));   // a stale child is removed first
            Assert.Contains(h.Status, s => s.Contains("restored"));
            Assert.Equal(VhfButtons.South, h.Parse().Buttons);        // the held button is sent again on the new controller
        }

        [Fact]
        public void ARecoveryThatFailsIsRetriedNoMoreOftenThanTheInterval()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Pad.SubmitReport();
            h.Transport.SubmitResult = VhfWin32.ErrorDeviceRemoved;
            h.Now = 150;
            h.Pad.SubmitReport();
            Assert.True(h.Pad.ControllerLost);

            h.Transport.CreateResult = VhfWin32.ErrorNotFound;         // the driver is gone for now
            int before = h.Transport.Count(VhfProtocol.IoctlCreateController);
            h.Now = 160; h.Pad.SubmitReport();                         // first attempt fails
            h.Now = 170; h.Pad.SubmitReport();                         // too soon: no new attempt
            Assert.Equal(before + 1, h.Transport.Count(VhfProtocol.IoctlCreateController));
            Assert.True(h.Pad.ControllerLost);

            h.Transport.SubmitResult = 0;
            h.Transport.CreateResult = 0;
            h.Now = 700; h.Pad.SubmitReport();                         // interval passed: reconnects and recreates
            Assert.False(h.Pad.ControllerLost);
        }

        [Fact]
        public void FeedbackIsPolledPeriodicallyAndRaisedForDisplay()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Transport.Feedback.Enqueue(FakeVhfTransport.XboxRumble(0, 65535, 0, 0, 0));
            for (int i = 0; i < 4; i++) { h.Now += 10; h.Pad.SubmitReport(); }
            Assert.Single(h.Feedback);
            Assert.Equal(65535, h.Feedback[0].LowFrequency);
            Assert.Same(h.Feedback[0], h.Pad.LastFeedback);
        }

        [Fact]
        public void AnOrdinarySubmitErrorIsReportedOnceNotEveryTick()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Transport.SubmitResult = VhfWin32.ErrorAccessDenied;
            for (int i = 0; i < 5; i++) { h.Now += 10; h.Pad.SubmitReport(); }
            Assert.Single(h.Status);
            Assert.False(h.Pad.ControllerLost);
            Assert.Equal(VhfWin32.ErrorAccessDenied, h.Pad.LastError);
        }
    }
}
