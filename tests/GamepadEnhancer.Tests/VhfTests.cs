using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        private readonly object gate = new();   // the engine thread and the test thread both touch the fake

        public int Open() { if (OpenResult != 0) return OpenResult; IsOpen = true; return 0; }
        public void Close() { IsOpen = false; CloseCount++; }
        public void Dispose() => Close();

        public int Ioctl(uint code, byte[] input, byte[] output, out int bytesReturned)
        {
            lock (gate) return IoctlLocked(code, input, output, out bytesReturned);
        }

        private int IoctlLocked(uint code, byte[] input, byte[] output, out int bytesReturned)
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

        public int Count(uint code) { lock (gate) return Calls.Count(c => c.Code == code); }
        public byte[] LastInput() { lock (gate) return Calls.Last(c => c.Code == VhfProtocol.IoctlSubmitInputState).Input; }

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
            h.Now = 25;
            h.Pad.Pump();                    // the engine calls this every tick, also while the pad is idle
            Assert.Single(h.Feedback);
            Assert.Equal(65535, h.Feedback[0].LowFrequency);
            Assert.Same(h.Feedback[0], h.Pad.LastFeedback);
        }

        [Fact]
        public void FeedbackIsNotPolledMoreOftenThanEvery20Ms()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Now = 25; h.Pad.Pump();
            int polls = h.Transport.Count(VhfProtocol.IoctlPollFeedback);
            h.Now = 30; h.Pad.Pump();
            h.Now = 40; h.Pad.Pump();
            Assert.Equal(polls, h.Transport.Count(VhfProtocol.IoctlPollFeedback));
            h.Now = 46; h.Pad.Pump();
            Assert.Equal(polls + 1, h.Transport.Count(VhfProtocol.IoctlPollFeedback));
        }

        [Fact]
        public void PumpBringsALostControllerBackWithoutWaitingForAReport()
        {
            var h = new Harness(VhfProfileId.XboxSeries);
            h.Pad.Connect();
            h.Pad.SubmitReport();
            h.Transport.SubmitResult = VhfWin32.ErrorDeviceRemoved;
            h.Now = 150;
            h.Pad.SubmitReport();
            Assert.True(h.Pad.ControllerLost);
            h.Transport.SubmitResult = 0;
            h.Now = 160;
            h.Pad.Pump();
            Assert.False(h.Pad.ControllerLost);
            Assert.Contains(h.Status, s => s.Contains("restored"));
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

    public class VhfEngineAndSettingsTests
    {
        private static bool WaitFor(Func<bool> condition, int ms = 3000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (condition()) return true; System.Threading.Thread.Sleep(5); }
            return condition();
        }

        [Fact]
        public void ViGEmIsTheDefaultBackendAndOldSettingsFilesKeepIt()
        {
            var fresh = new AppSettings();
            Assert.Equal("vigem", fresh.OutputBackend);
            Assert.Equal("auto", fresh.VhfProfile);

            // a settings.json written before the VHF backend existed has none of the new fields
            var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"InputCaptureMethod\":\"RawInput\",\"ActiveGame\":\"apex\"}");
            Assert.Equal("vigem", old.OutputBackend);
            Assert.Equal("RawInput", old.InputCaptureMethod);

            var chosen = new AppSettings { OutputBackend = "vhf", VhfProfile = "dualsense" };
            var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(chosen));
            Assert.Equal("vhf", back.OutputBackend);
            Assert.Equal("dualsense", back.VhfProfile);
        }

        [Fact]
        public void TheProfileFollowsTheLayoutFamilyAndFallsBackToItsDefault()
        {
            Assert.Equal(VhfProfileId.XboxSeries, VhfProfiles.Resolve("auto", PadFamily.Xbox360));
            Assert.Equal(VhfProfileId.XboxOne, VhfProfiles.Resolve("xbox_one", PadFamily.Xbox360));
            Assert.Equal(VhfProfileId.DualShock4, VhfProfiles.Resolve("auto", PadFamily.DualShock4));
            Assert.Equal(VhfProfileId.DualSense, VhfProfiles.Resolve("dualsense", PadFamily.DualShock4));
            // a choice that does not fit the layout is not applied
            Assert.Equal(VhfProfileId.XboxSeries, VhfProfiles.Resolve("dualsense", PadFamily.Xbox360));
            Assert.Equal(VhfProfileId.DualShock4, VhfProfiles.Resolve("xbox_one", PadFamily.DualShock4));
            Assert.Equal(VhfProfileId.DualShock4, VhfProfiles.Resolve(null, PadFamily.DualShock4));
            Assert.Equal(new[] { "xbox_series", "xbox_one" }, VhfProfiles.For(PadFamily.Xbox360).Select(p => p.Id).ToArray());
            Assert.Equal(new[] { "dualshock4", "dualsense" }, VhfProfiles.For(PadFamily.DualShock4).Select(p => p.Id).ToArray());
            // Xbox 360 is never offered: VHF cannot make an XUSB device
            Assert.DoesNotContain(VhfProfiles.For(PadFamily.Xbox360), p => p.Name.Contains("360"));
        }

        [Fact]
        public void EngineCreatesTheVhfPadSendsReportsAndRemovesItOnDisconnect()
        {
            var fake = new FakeVhfTransport();
            using var engine = new ControllerEngine();
            engine.Connect(true, null, new VhfTarget(VhfProfileId.XboxSeries, () => fake));
            Assert.True(engine.IsConnected);
            Assert.True(engine.IsVhf);
            Assert.Equal("VHF", engine.Backend);
            Assert.False(engine.IsRemote);
            Assert.Equal(1, fake.Count(VhfProtocol.IoctlCreateController));
            Assert.True(WaitFor(() => fake.Count(VhfProtocol.IoctlSubmitInputState) > 0), "no report reached the driver");

            engine.Disconnect();
            Assert.False(engine.IsConnected);
            Assert.Equal("ViGEm", engine.Backend);
            Assert.Equal(1, fake.Count(VhfProtocol.IoctlDestroyController));
            Assert.False(fake.IsOpen);
        }

        [Fact]
        public void EngineCreatesADualSenseWhenAskedAndPollsItsFeedbackEvenWhileIdle()
        {
            var fake = new FakeVhfTransport();
            var got = new List<VhfFeedback>();
            using var engine = new ControllerEngine();
            engine.FeedbackReceived += f => { lock (got) got.Add(f); };
            engine.Connect(false, null, new VhfTarget(VhfProfileId.DualSense, () => fake));
            byte[] create = fake.Calls.Single(c => c.Code == VhfProtocol.IoctlCreateController).Input;
            Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(create.AsSpan(12)));

            fake.Feedback.Enqueue(FakeVhfTransport.XboxRumble(0, 1000, 2000, 0, 0));
            Assert.True(WaitFor(() => { lock (got) return got.Count > 0; }), "feedback was not read");
            engine.Disconnect();
        }

        [Fact]
        public void AMissingDriverFailsTheConnectWithAMessageAndLeavesTheEngineStopped()
        {
            var fake = new FakeVhfTransport { OpenResult = VhfWin32.ErrorNotFound };
            using var engine = new ControllerEngine();
            var ex = Assert.Throws<VhfException>(() => engine.Connect(true, null, new VhfTarget(VhfProfileId.XboxSeries, () => fake)));
            Assert.Contains("not installed", ex.Message);
            Assert.False(engine.IsConnected);
            Assert.Equal("ViGEm", engine.Backend);   // nothing was switched silently
        }
    }

    internal sealed class FakeSignatureReader : IVhfSignatureReader
    {
        public VhfSignature Catalog = Good();
        public VhfSignature Tool = Good();
        public static VhfSignature Good(string subject = "CN=Example Publisher") => new(VhfSignatureKind.Trusted, subject, "CN=Public CA", "AB12", "ok");
        public static VhfSignature Of(VhfSignatureKind kind) => new(kind, "CN=Someone", "CN=Someone Else", "00", "test");
        public VhfSignature Read(string path) => path.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ? Catalog : Tool;
    }

    internal sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-vhf-" + Guid.NewGuid().ToString("N"));
        public TempDir() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public class VhfPackageVerifierTests
    {
        private static readonly string[] Payload =
        {
            "driver/VibeshineVhfGamepad.inf", "driver/VibeshineVhfGamepad.dll", "driver/VibeshineVhfGamepad.cat", "tools/VibeshineVhfGamepadDeviceSetup.exe"
        };

        /// <summary>Writes a package folder the way upstream lays it out, with a manifest whose hashes are right unless told otherwise.</summary>
        private static void MakePackage(string root, string platform = "x64", int protocol = 2, int schema = 1, bool omitFromManifest = false, bool badJson = false)
        {
            foreach (string rel in Payload)
            {
                string path = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, "content of " + rel);
            }
            var files = new List<object>();
            foreach (string rel in Payload.Skip(omitFromManifest ? 1 : 0))
                files.Add(new { path = rel, sha256 = VhfPackageVerifier.Sha256Of(System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))) });
            files.Add(new { path = "mouse_mapper/VibeshineVhfMouseMapper.inf", sha256 = new string('a', 64) });   // a file this app never installs and the package omits
            string manifest = badJson ? "{ not json" : JsonSerializer.Serialize(new
            {
                schema_version = schema,
                source_revision = "93dc007a608b341aa998378221f8958f045ee7f2",
                driver_ver = "11/14/2026,0.1.0.11",
                platform,
                protocol_version = protocol,
                files
            });
            File.WriteAllText(System.IO.Path.Combine(root, "manifest.json"), manifest);
        }

        private static VhfPackageReport Check(Action<string> arrange, FakeSignatureReader reader = null, string platform = "x64")
        {
            using var dir = new TempDir();
            arrange(dir.Path);
            return new VhfPackageVerifier(reader ?? new FakeSignatureReader(), platform).Verify(dir.Path);
        }

        [Fact]
        public void AValidSignedPackagePassesAndCanBeInstalledThroughTheElevatedSetupTool()
        {
            using var dir = new TempDir();
            MakePackage(dir.Path);
            VhfPackageReport report = new VhfPackageVerifier(new FakeSignatureReader(), "x64").Verify(dir.Path);
            Assert.True(report.Ok, report.Summary());
            Assert.Equal("11/14/2026,0.1.0.11", report.DriverVer);
            Assert.Equal(2, report.ProtocolVersion);

            var install = VhfInstaller.InstallStartInfo(report);
            Assert.EndsWith("VibeshineVhfGamepadDeviceSetup.exe", install.FileName);
            Assert.Contains("install --inf", install.Arguments);
            Assert.Contains("VibeshineVhfGamepad.inf", install.Arguments);
            Assert.Equal("runas", install.Verb);            // always the normal Windows administrator prompt
            Assert.True(install.UseShellExecute);
            Assert.Equal("remove", VhfInstaller.RemoveStartInfo(report).Arguments);
        }

        [Theory]
        [InlineData((int)VhfSignatureKind.Unsigned, "Not signed")]
        [InlineData((int)VhfSignatureKind.SelfSigned, "Self-signed")]
        [InlineData((int)VhfSignatureKind.TestCertificate, "test certificate")]
        [InlineData((int)VhfSignatureKind.UntrustedChain, "does not trust")]
        [InlineData((int)VhfSignatureKind.Tampered, "does not match its signature")]
        [InlineData((int)VhfSignatureKind.Expired, "expired")]
        public void ACatalogThatIsNotProductionSignedIsRefused(int kind, string text)
        {
            var reader = new FakeSignatureReader { Catalog = FakeSignatureReader.Of((VhfSignatureKind)kind) };
            VhfPackageReport report = Check(root => MakePackage(root), reader);
            Assert.False(report.Ok);
            Assert.Contains(report.Problems, p => p.StartsWith("The driver catalog") && p.Contains(text));
            Assert.Throws<InvalidOperationException>(() => VhfInstaller.InstallStartInfo(report));
        }

        [Fact]
        public void AnUnsignedSetupToolIsRefusedEvenWithASignedCatalog()
        {
            var reader = new FakeSignatureReader { Tool = FakeSignatureReader.Of(VhfSignatureKind.Unsigned) };
            VhfPackageReport report = Check(root => MakePackage(root), reader);
            Assert.False(report.Ok);
            Assert.Contains(report.Problems, p => p.StartsWith("The setup tool"));
        }

        [Fact]
        public void AChangedFileIsRefusedBecauseItNoLongerMatchesTheManifest()
        {
            VhfPackageReport report = Check(root => { MakePackage(root); File.AppendAllText(System.IO.Path.Combine(root, "driver", "VibeshineVhfGamepad.dll"), "x"); });
            Assert.False(report.Ok);
            Assert.Contains(report.Problems, p => p.Contains("VibeshineVhfGamepad.dll") && p.Contains("SHA-256"));
        }

        [Fact]
        public void MissingAndUnexpectedFilesAreRefused()
        {
            VhfPackageVerifier v = new(new FakeSignatureReader(), "x64");
            using (var dir = new TempDir())
            {
                MakePackage(dir.Path);
                File.Delete(System.IO.Path.Combine(dir.Path, "driver", "VibeshineVhfGamepad.cat"));
                Assert.Contains(v.Verify(dir.Path).Problems, p => p.StartsWith("Missing file") && p.Contains(".cat"));
            }
            using (var dir = new TempDir())
            {
                MakePackage(dir.Path);
                File.WriteAllText(System.IO.Path.Combine(dir.Path, "extra.dll"), "evil");
                Assert.Contains(v.Verify(dir.Path).Problems, p => p.StartsWith("Unexpected file") && p.Contains("extra.dll"));
            }
            Assert.False(v.Verify(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-does-not-exist-" + Guid.NewGuid().ToString("N"))).Ok);
        }

        [Fact]
        public void TheManifestMustMatchThisProtocolProcessorAndSchema()
        {
            Assert.Contains(Check(root => MakePackage(root, protocol: 3)).Problems, p => p.Contains("protocol 3"));
            Assert.Contains(Check(root => MakePackage(root, platform: "ARM64")).Problems, p => p.Contains("ARM64") && p.Contains("x64"));
            Assert.Contains(Check(root => MakePackage(root, schema: 2)).Problems, p => p.Contains("schema_version"));
            Assert.Contains(Check(root => MakePackage(root, omitFromManifest: true)).Problems, p => p.Contains("not listed"));
            Assert.Contains(Check(root => MakePackage(root, badJson: true)).Problems, p => p.Contains("not valid JSON"));
        }

        [Fact]
        public void AZipIsUnpackedSafelyAndThenVerifiedLikeAFolder()
        {
            using var source = new TempDir();
            MakePackage(source.Path);
            using var work = new TempDir();
            string zip = System.IO.Path.Combine(work.Path, "package.zip");
            ZipFile.CreateFromDirectory(source.Path, zip);

            string unpacked = VhfPackageVerifier.ExtractZip(zip, System.IO.Path.Combine(work.Path, "out"));
            Assert.True(new VhfPackageVerifier(new FakeSignatureReader(), "x64").Verify(unpacked).Ok);
        }

        [Fact]
        public void AZipThatWritesOutsideItsFolderOrHasTooManyFilesIsRefused()
        {
            using var work = new TempDir();
            string evil = System.IO.Path.Combine(work.Path, "evil.zip");
            using (var stream = File.Create(evil))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open());
                writer.Write("x");
            }
            Assert.Throws<InvalidDataException>(() => VhfPackageVerifier.ExtractZip(evil, System.IO.Path.Combine(work.Path, "out1")));
            Assert.False(File.Exists(System.IO.Path.Combine(work.Path, "escaped.txt")));

            string many = System.IO.Path.Combine(work.Path, "many.zip");
            using (var stream = File.Create(many))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                for (int i = 0; i < 20; i++) using (var w = new StreamWriter(zip.CreateEntry("f" + i + ".txt").Open())) w.Write("x");
            Assert.Throws<InvalidDataException>(() => VhfPackageVerifier.ExtractZip(many, System.IO.Path.Combine(work.Path, "out2")));
        }
    }

    public class VhfSignatureTests
    {
        [Fact]
        public void TheTrustResultAndTheSignerNameDecideWhetherASignatureIsProduction()
        {
            VhfSignature Classify(int result, string subject, string issuer) => WinTrustSignatureReader.Classify(result, subject, issuer, "T");
            Assert.Equal(VhfSignatureKind.Unsigned, Classify(unchecked((int)0x800B0100), "", "").Kind);                          // TRUST_E_NOSIGNATURE
            Assert.Equal(VhfSignatureKind.Tampered, Classify(unchecked((int)0x80096010), "CN=Acme", "CN=Public CA").Kind);       // TRUST_E_BAD_DIGEST
            Assert.Equal(VhfSignatureKind.SelfSigned, Classify(0, "CN=Acme", "CN=Acme").Kind);                                   // trusted locally, but signed itself
            Assert.Equal(VhfSignatureKind.TestCertificate, Classify(0, "CN=Vibeshine VHF Gamepad Test", "CN=Public CA").Kind);
            Assert.Equal(VhfSignatureKind.TestCertificate, Classify(0, "CN=Acme", "CN=WDKTestCert someone").Kind);
            Assert.Equal(VhfSignatureKind.UntrustedChain, Classify(unchecked((int)0x800B0109), "CN=Acme", "CN=Unknown CA").Kind);   // CERT_E_UNTRUSTEDROOT
            Assert.Equal(VhfSignatureKind.Expired, Classify(unchecked((int)0x800B0101), "CN=Acme", "CN=Public CA").Kind);
            Assert.Equal(VhfSignatureKind.Trusted, Classify(0, "CN=Acme Software Ltd", "CN=Public CA").Kind);
            Assert.True(Classify(0, "CN=Contest Media", "CN=Public CA").IsProduction);                                           // "test" inside a word is not a test certificate
        }

        [Fact]
        public void TestNamesAreRecognisedWithoutFlaggingOrdinaryWords()
        {
            foreach (string name in new[] { "CN=Vibeshine VHF Gamepad Test", "CN=WDKTestCert me,123", "CN=Test Signing", "O=Acme, CN=acme test" })
                Assert.True(WinTrustSignatureReader.LooksLikeTestCertificate(name), name);
            foreach (string name in new[] { "CN=Contest Media", "CN=Latest Software", "CN=Microsoft Windows Hardware Compatibility Publisher", "" })
                Assert.False(WinTrustSignatureReader.LooksLikeTestCertificate(name), name);
        }

        [Fact]
        public void TheRealReaderSeesAnUnsignedFileAsUnsigned()
        {
            using var dir = new TempDir();
            string file = System.IO.Path.Combine(dir.Path, "plain.exe");
            File.WriteAllBytes(file, new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
            VhfSignature sig = new WinTrustSignatureReader().Read(file);
            Assert.Equal(VhfSignatureKind.Unsigned, sig.Kind);
            Assert.False(sig.IsProduction);
            Assert.Equal(VhfSignatureKind.Unsigned, new WinTrustSignatureReader().Read(System.IO.Path.Combine(dir.Path, "missing.cat")).Kind);
        }

        [Fact]
        public void TheRealReaderTrustsAMicrosoftSignedExecutable()
        {
            // dotnet.exe carries an embedded Microsoft signature; this proves the WinVerifyTrust call itself works end to end.
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            string dotnet = pf == null ? null : System.IO.Path.Combine(pf, "dotnet", "dotnet.exe");
            if (dotnet == null || !File.Exists(dotnet)) return;   // nothing known to be signed on this machine
            VhfSignature sig = new WinTrustSignatureReader().Read(dotnet);
            Assert.True(sig.Kind == VhfSignatureKind.Trusted, "dotnet.exe: " + sig.Kind + " " + sig.Detail + " " + sig.Subject);
            Assert.Contains("Microsoft", sig.Subject);
        }

        [Fact]
        public void TheRealReaderTrustsAWindowsCatalog()
        {
            // The catalog path of the verifier is the one that matters most: check it against a catalog Microsoft ships.
            string folder = System.IO.Path.Combine(Environment.SystemDirectory, "CatRoot", "{F750E6C3-38EE-11D1-85E5-00C04FC295EE}");
            string cat = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.cat").FirstOrDefault() : null;
            if (cat == null) return;
            VhfSignature sig = new WinTrustSignatureReader().Read(cat);
            Assert.True(sig.Kind == VhfSignatureKind.Trusted, System.IO.Path.GetFileName(cat) + ": " + sig.Kind + " " + sig.Detail + " " + sig.Subject);
        }
    }

    public class VhfDriverInspectorTests
    {
        private sealed class FakeDevices : IVhfDeviceInfoSource
        {
            public VhfInstalledDevice Device;
            public Exception Throw;
            public VhfInstalledDevice Find() { if (Throw != null) throw Throw; return Device; }
        }

        private static VhfInstalledDevice Installed(string catalog = null) =>
            new() { DriverVersion = "0.1.0.11", DriverProvider = "Example Publisher", DriverDate = "2026-11-14", CatalogPath = catalog };

        private static VhfDriverStatus Run(FakeDevices devices, FakeVhfTransport transport, FakeSignatureReader reader = null, bool owned = false) =>
            new VhfDriverInspector(devices, () => transport, reader ?? new FakeSignatureReader()).Inspect(owned);

        [Fact]
        public void NothingInstalledIsReportedAsNotInstalledAndViGEmStaysTheDefault()
        {
            var status = Run(new FakeDevices(), new FakeVhfTransport { OpenResult = VhfWin32.ErrorNotFound });
            Assert.Equal(VhfDriverState.NotInstalled, status.State);
            Assert.False(status.IsReady);
            Assert.Contains("ViGEm", status.Headline);
            Assert.Contains("not installed", status.SignatureLine);
        }

        [Fact]
        public void ADeviceWithoutAControlInterfaceIsInstalledButNotRunning()
        {
            var status = Run(new FakeDevices { Device = Installed() }, new FakeVhfTransport { OpenResult = VhfWin32.ErrorNotFound });
            Assert.Equal(VhfDriverState.InstalledNotRunning, status.State);
            Assert.Equal("0.1.0.11", status.DriverVersion);
        }

        [Fact]
        public void ARunningDriverShowsVersionProviderProfilesAndTheSignatureOfItsCatalog()
        {
            var reader = new FakeSignatureReader { Catalog = FakeSignatureReader.Good("CN=Example Publisher") };
            var status = Run(new FakeDevices { Device = Installed(@"C:\store\driver.cat") }, new FakeVhfTransport(), reader);
            Assert.Equal(VhfDriverState.Ready, status.State);
            Assert.True(status.IsReady);
            Assert.Contains("Xbox Series", status.Profiles);
            Assert.Contains("DualSense", status.Profiles);
            Assert.DoesNotContain("360", status.Profiles);
            Assert.Contains("0.1.0.11", status.VersionLine);
            Assert.Contains("Example Publisher", status.VersionLine);
            Assert.Contains("Example Publisher", status.SignatureLine);
        }

        [Fact]
        public void ATestSignedInstalledDriverIsShownAsNotAProductionSignature()
        {
            var reader = new FakeSignatureReader { Catalog = FakeSignatureReader.Of(VhfSignatureKind.TestCertificate) };
            var status = Run(new FakeDevices { Device = Installed(@"C:\store\driver.cat") }, new FakeVhfTransport(), reader);
            Assert.Contains("test certificate", status.SignatureLine);
            Assert.False(status.Signature.IsProduction);
        }

        [Fact]
        public void AProtocolMismatchAndABusyInterfaceAreTold()
        {
            Assert.Equal(VhfDriverState.ProtocolMismatch, Run(new FakeDevices { Device = Installed() }, new FakeVhfTransport { MaxVersion = 1 }).State);
            var busy = Run(new FakeDevices { Device = Installed() }, new FakeVhfTransport { OpenResult = 32 });
            Assert.Equal(VhfDriverState.InUse, busy.State);
            Assert.True(busy.IsReady);
        }

        [Fact]
        public void WhileThisAppOwnsTheDriverTheHandshakeIsSkipped()
        {
            var transport = new FakeVhfTransport();
            var status = Run(new FakeDevices { Device = Installed() }, transport, owned: true);
            Assert.Equal(VhfDriverState.Ready, status.State);
            Assert.Empty(transport.Calls);
        }

        [Fact]
        public void AFailureToAskWindowsIsAnErrorWithTheReason()
        {
            var status = Run(new FakeDevices { Throw = new Win32Exception(5, "Access is denied") }, new FakeVhfTransport());
            Assert.Equal(VhfDriverState.Error, status.State);
            Assert.Contains("Access is denied", status.Headline);
        }

        [Fact]
        public void TheRealInspectorRunsWithoutThrowingWhetherOrNotTheDriverIsInstalled()
        {
            VhfDriverStatus status = VhfDriverInspector.CreateDefault().Inspect();
            Assert.NotEqual(VhfDriverState.Error, status.State);
            Assert.False(string.IsNullOrEmpty(status.Headline));
        }
    }
}
