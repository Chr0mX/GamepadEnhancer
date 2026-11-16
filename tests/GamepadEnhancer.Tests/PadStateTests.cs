using System;
using System.Collections.Generic;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class PadStateMathTests
    {
        private static int Bit(string action) => 1 << Array.IndexOf(ControllerEngine.BindingActions, action);

        [Fact]
        public void GuideActionIsAppendedSoExistingBitsKeepTheirIndex()
        {
            string[] a = ControllerEngine.BindingActions;
            Assert.Equal(ControllerEngine.ActionGuidePs, a[^1]);
            Assert.Equal(ControllerEngine.ActionACross, a[0]);
            Assert.Equal(ControllerEngine.ActionDPadRight, a[PadStateMath.ActionDpadRight]);
            Assert.Equal(ControllerEngine.ActionLtL2, a[PadStateMath.ActionLt]);
            Assert.Equal(ControllerEngine.ActionRtR2, a[PadStateMath.ActionRt]);
        }

        [Fact]
        public void AxisConversionsReachBothEndsAndCentre()
        {
            Assert.Equal(1.0, PadStateMath.FromXboxAxis(short.MaxValue));
            Assert.Equal(-1.0, PadStateMath.FromXboxAxis(short.MinValue));
            Assert.Equal(0.0, PadStateMath.FromXboxAxis(0));
            Assert.Equal(0.0, PadStateMath.FromDs4Axis(128));
            Assert.Equal(1.0, PadStateMath.FromDs4Axis(255));
            Assert.Equal(-1.0, PadStateMath.FromDs4Axis(0));    // clamped (128 counts below centre, 127 above)
            Assert.Equal(1.0, PadStateMath.FromPercent(150));
            Assert.Equal(0.0, PadStateMath.FromPercent(double.NaN));
        }

        [Fact]
        public void RemoteYIsFlippedToUpAndTriggersScale()
        {
            Assert.Equal(-1.0, PadStateMath.FromRemoteYUp(32767), 3);   // +Y down in the protocol
            Assert.Equal(1.0, PadStateMath.FromRemoteYUp(-32768), 3);
            Assert.Equal(1.0, PadStateMath.FromRemoteTrigger(1023));
            Assert.Equal(0.0, PadStateMath.FromRemoteTrigger(0));
        }

        [Fact]
        public void Ds4CancelsOpposingDpadDirectionsAndXboxDoesNot()
        {
            int up = Bit(ControllerEngine.ActionDPadUp), down = Bit(ControllerEngine.ActionDPadDown);
            int left = Bit(ControllerEngine.ActionDPadLeft), a = Bit(ControllerEngine.ActionACross);
            Assert.Equal(a | left, PadStateMath.FinalMask(a | up | down | left, xbox: false));
            Assert.Equal(a | up | down | left, PadStateMath.FinalMask(a | up | down | left, xbox: true));
            Assert.Equal(up, PadStateMath.FinalMask(up, xbox: false));
        }

        [Fact]
        public void RelaxEasesOnlyTowardTheCentre()
        {
            Assert.Equal(1.0, PadStateMath.Relax(0.2, 1.0, 16, 45));        // moving outward: instant
            Assert.Equal(-0.5, PadStateMath.Relax(0.5, -0.5, 16, 45));      // crossing the centre: instant
            double eased = PadStateMath.Relax(1.0, 0.0, 16, 45);
            Assert.InRange(eased, 0.01, 0.99);                               // returning: partway
            Assert.Equal(0.0, PadStateMath.Relax(0.001, 0.0, 16, 45));       // snaps once close enough
            Assert.Equal(0.0, PadStateMath.Relax(1.0, 0.0, 16, 0));          // no easing configured
        }

        [Fact]
        public void BuildGivesDifferentValuesForEachStage()
        {
            // PS4: engine-native +Y is down. Drift pulls the right stick down by 32 %, the final report rounds it.
            byte ry = StickMath.PercentToDs4Axis(32.0);
            var (physical, processed, final) = PadStateMath.Build(
                xbox: false, RemoteFrame.None,
                rawMask: Bit(ControllerEngine.ActionACross), mask: Bit(ControllerEngine.ActionACross) | Bit(ControllerEngine.ActionLtL2),
                mouseX: 0, mouseY: 0, processedX: 0, processedY: 32.0,
                finalRightX: PadStateMath.FromDs4Axis(128), finalRightY: PadStateMath.FromDs4Axis(ry),
                padLeftX: 0, padLeftY: 0);

            Assert.Equal(0.0, physical.Ry);                       // the user's devices asked for nothing on the stick
            Assert.Equal(-0.32, processed.Ry, 3);                 // PS4 +Y is down, so down is negative in the +up state
            Assert.Equal(PadStateMath.NativeYToUp(false, PadStateMath.FromDs4Axis(ry)), final.Ry, 6);
            Assert.True(final.Ry < 0);
            Assert.Equal(0.0, physical.Lt);
            Assert.Equal(1.0, processed.Lt);                      // LT bound and pressed in the processed mask only
            Assert.True(physical.Has(0) && processed.Has(0) && final.Has(0));
        }

        [Fact]
        public void XboxYIsAlreadyUpAndMouseLookShowsAsPhysicalInput()
        {
            var (physical, processed, final) = PadStateMath.Build(
                xbox: true, RemoteFrame.None, 0, 0,
                mouseX: 50, mouseY: 25, processedX: 50, processedY: 25,
                finalRightX: PadStateMath.FromXboxAxis(StickMath.PercentToXboxAxis(50.0)),
                finalRightY: PadStateMath.FromXboxAxis(StickMath.PercentToXboxAxis(25.0)),
                padLeftX: 0, padLeftY: 0);
            Assert.Equal(0.5, physical.Rx, 3);
            Assert.Equal(0.25, physical.Ry, 3);                    // positive = up on Xbox, unchanged
            Assert.Equal(0.5, processed.Rx, 3);
            Assert.Equal(0.5, final.Rx, 3);
        }

        [Fact]
        public void RemoteControlOverridesTheRightStickAndTriggersInEveryView()
        {
            var rf = new RemoteFrame(true, true, 16384, 16384, -32768, 32767, 1023, 0, 0);
            var (physical, processed, final) = PadStateMath.Build(
                xbox: false, rf, 0, 0, 0, 0, 0, 0,
                PadStateMath.FromRemoteX(-32768), PadStateMath.FromDs4Axis(StickMath.Ds4Center),
                padLeftX: PadStateMath.FromRemoteX(16384), padLeftY: PadStateMath.FromRemoteYUp(16384));

            Assert.Equal(-1.0, physical.Rx, 3);
            Assert.Equal(-1.0, physical.Ry, 3);                    // remote +Y is down -> negative (up-positive)
            Assert.Equal(0.5, physical.Lx, 3);
            Assert.Equal(1.0, processed.Lt, 3);
            Assert.Equal(1.0, final.Lt, 3);
            Assert.Equal(0.0, final.Rt, 3);
            Assert.Equal(-0.5, final.Ly, 3);
        }
    }

    public class PadMonitorTests
    {
        private static PadState S(int mask, double rx = 0) => new PadState(0, 0, rx, 0, 0, 0, mask);

        [Fact]
        public void EnabledOnlyWhileAViewerIsRegistered()
        {
            var m = new PadMonitor();
            Assert.False(m.Enabled);
            m.AddViewer(); m.AddViewer();
            Assert.True(m.Enabled);
            m.RemoveViewer();
            Assert.True(m.Enabled);
            m.RemoveViewer();
            Assert.False(m.Enabled);
            m.RemoveViewer();                     // never goes negative
            m.AddViewer();
            Assert.True(m.Enabled);
        }

        [Fact]
        public void PublishUpdatesViewsAndBumpsVersionOnlyOnChange()
        {
            var m = new PadMonitor();
            long v0 = m.Version;
            m.Publish(S(0), S(1), S(2, 0.5), 10);
            long v1 = m.Version;
            Assert.True(v1 > v0);
            Assert.Equal(0, m.Get(PadView.Physical).Mask);
            Assert.Equal(1, m.Get(PadView.Processed).Mask);
            Assert.Equal(0.5, m.Get(PadView.Final).Rx);
            m.Publish(S(0), S(1), S(2, 0.5), 20);   // identical content
            Assert.Equal(v1, m.Version);
        }

        [Fact]
        public void ButtonEdgesAreRecordedPerViewAndEachReaderHasItsOwnCursor()
        {
            var m = new PadMonitor();
            m.Publish(S(0), S(0), S(1), 5);          // A pressed in Final only
            m.Publish(S(0), S(0), S(0), 9);          // released
            long c1 = 0, c2 = 0;
            var b1 = new PadEvent[16];
            var b2 = new PadEvent[16];
            int n1 = m.CopyEventsSince(ref c1, b1);
            Assert.Equal(2, n1);
            Assert.Equal(PadView.Final, b1[0].View);
            Assert.True(b1[0].Pressed);
            Assert.False(b1[1].Pressed);
            Assert.Equal(0, m.CopyEventsSince(ref c1, b1));            // nothing new for reader 1
            Assert.Equal(2, m.CopyEventsSince(ref c2, b2));            // reader 2 still sees both
        }

        [Fact]
        public void RingKeepsOnlyTheRecentEventsForASlowReader()
        {
            var m = new PadMonitor();
            for (int i = 0; i < PadMonitor.RingSize * 2; i++) m.Publish(S(0), S(0), S((i & 1) == 0 ? 1 : 0), i);
            long cursor = 0;
            var buf = new PadEvent[PadMonitor.RingSize];
            int n = m.CopyEventsSince(ref cursor, buf);
            Assert.InRange(n, 1, PadMonitor.RingSize);
            Assert.Equal(m.EventCount, cursor);
            // events come back oldest first with strictly increasing ticks
            for (int i = 1; i < n; i++) Assert.True(buf[i].TickMs > buf[i - 1].TickMs);
        }

        [Fact]
        public void ResetReturnsEverythingToNeutral()
        {
            var m = new PadMonitor();
            m.Publish(S(3), S(3), S(3, 1.0), 1);
            m.NoteReport();
            m.Reset(PadFamily.Xbox360, true, false);
            Assert.Equal(PadFamily.Xbox360, m.Family);
            Assert.True(m.Connected);
            Assert.False(m.IsRemote);
            Assert.Equal(0, m.Get(PadView.Final).Mask);
            Assert.Equal(0, m.ReportsSent);
        }
    }

    public class PadLabelsTests
    {
        [Fact]
        public void EachFamilyHasItsOwnNamesForEveryAction()
        {
            int count = ControllerEngine.BindingActions.Length;
            for (int i = 0; i < count; i++)
            {
                Assert.False(string.IsNullOrWhiteSpace(PadLabels.For(PadFamily.Xbox360, i)));
                Assert.False(string.IsNullOrWhiteSpace(PadLabels.For(PadFamily.DualShock4, i)));
            }
            Assert.Equal("A", PadLabels.For(PadFamily.Xbox360, 0));
            Assert.Equal("Cross", PadLabels.For(PadFamily.DualShock4, 0));
            Assert.Equal("View", PadLabels.For(PadFamily.Xbox360, 8));
            Assert.Equal("Share", PadLabels.For(PadFamily.DualShock4, 8));
            Assert.Equal("Menu", PadLabels.For(PadFamily.Xbox360, 9));
            Assert.Equal("Options", PadLabels.For(PadFamily.DualShock4, 9));
            Assert.Equal("Guide", PadLabels.For(PadFamily.Xbox360, count - 1));
            Assert.Equal("PS", PadLabels.For(PadFamily.DualShock4, count - 1));
        }
    }

    public class BlockDeviceSelectionTests
    {
        private static EngineSettings Settings() => new()
        {
            MouseLookEnabled = true,
            Bindings = new Dictionary<string, BindingSetting>(StringComparer.OrdinalIgnoreCase)
            {
                [ControllerEngine.ActionACross] = new BindingSetting { Key = "Space" },
                [ControllerEngine.ActionBCircle] = new BindingSetting { Key = MouseInputNames.Left }
            }
        };

        [Fact]
        public void BothDevicesAreBlockedByDefault()
        {
            BlockSettings b = BlockSettings.From(Settings(), true, 0x91);
            Assert.NotEmpty(b.BlockedKeys);
            Assert.Contains(MouseInputNames.Left, b.BlockedMouseInputs);
            Assert.True(b.BlockMouseMove);
        }

        [Fact]
        public void KeyboardOnlyLeavesTheMouseAlone()
        {
            BlockSettings b = BlockSettings.From(Settings(), true, 0x91, blockKeyboard: true, blockMouse: false);
            Assert.NotEmpty(b.BlockedKeys);
            Assert.Empty(b.BlockedMouseInputs);
            Assert.False(b.BlockMouseMove);
        }

        [Fact]
        public void MouseOnlyLeavesTheKeyboardAlone()
        {
            BlockSettings b = BlockSettings.From(Settings(), true, 0x91, blockKeyboard: false, blockMouse: true);
            Assert.Empty(b.BlockedKeys);
            Assert.Contains(MouseInputNames.Left, b.BlockedMouseInputs);
            Assert.True(b.BlockMouseMove);
        }
    }
}

namespace GamepadEnhancer.Tests
{
    public class MouseStreamTests
    {
        private static string Run(string line, MakcuSession session, Func<int> mouse = null) =>
            MakcuCommandParser.Handle(line, new RemoteControlState(), session, 0, mouse);

        [Fact]
        public void SubscribeAndUnsubscribeAreSilentAndToggleTheSession()
        {
            var s = new MakcuSession();
            Assert.False(s.MouseStream);
            Assert.Null(Run("km.mouse_stream(1)", s));
            Assert.True(s.MouseStream);
            Assert.Null(Run("km.mouse_stream(0)", s));
            Assert.False(s.MouseStream);
        }

        [Fact]
        public void BadSubscribeArgumentsAreRejected()
        {
            var s = new MakcuSession();
            foreach (string bad in new[] { "km.mouse_stream(2)", "km.mouse_stream()", "km.mouse_stream(x)", "km.mouse_stream(1,0)" })
                Assert.Equal("ERR", Run(bad, s));
            Assert.False(s.MouseStream);
        }

        [Fact]
        public void QueryReturnsTheLocalMaskAndClientCannotSetIt()
        {
            var s = new MakcuSession();
            Assert.StartsWith("5", Run("km.mouse()", s, () => 5));
            Assert.StartsWith("0", Run("km.mouse()", s));              // no source = nothing held
            Assert.StartsWith("31", Run("km.mouse()", s, () => 0xFF)); // masked to five buttons
            Assert.Equal("ERR", Run("km.mouse(2)", s, () => 0));       // the server pushes it; a client does not set it
        }

        [Fact]
        public void PushHappensOnSubscribeChangeAndKeepAliveOnly()
        {
            Assert.False(MousePush.ShouldSend(false, 1, -1, 0, 0));                  // not subscribed
            Assert.True(MousePush.ShouldSend(true, 0, -1, 0, 0));                    // first push, even with nothing held
            Assert.False(MousePush.ShouldSend(true, 2, 2, 500, 0));                  // unchanged, keep-alive not due
            Assert.True(MousePush.ShouldSend(true, 3, 2, 10, 0));                    // changed
            Assert.True(MousePush.ShouldSend(true, 2, 2, MousePush.KeepAliveMs, 0)); // keep-alive due
        }
    }
}
