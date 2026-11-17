using System;
using System.Collections.Generic;
using System.Linq;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    internal sealed class FakeInjector : IInputInjector
    {
        public List<string> Log { get; } = new();
        public void Send(InjectedInput input, bool down) => Log.Add((down ? "+" : "-") + input.Name);
    }

    public class PaddleModelTests
    {
        [Fact]
        public void SlotsHaveStableNamesAndIds()
        {
            Assert.Equal(4, PaddleSlots.Count);
            Assert.Equal(new[] { "PADDLE_LEFT_UPPER", "PADDLE_LEFT_LOWER", "PADDLE_RIGHT_UPPER", "PADDLE_RIGHT_LOWER" }, PaddleSlots.Names);
            Assert.True(PaddleSlots.TryParseId("RightLower", out int slot));
            Assert.Equal(3, slot);
            Assert.False(PaddleSlots.TryParseId("Nope", out _));
            Assert.False(PaddleSlots.TryParseId("FACE_SOUTH", out _));
        }

        [Fact]
        public void PaddlesAreNotPartOfTheActionBitLayout()
        {
            // Adding paddles must not move existing action bits (Network API ids and saved masks depend on them).
            Assert.Equal(22, ControllerEngine.BindingActions.Length);
            Assert.DoesNotContain(ControllerEngine.BindingActions, a => a.StartsWith("Paddle", StringComparison.Ordinal));
        }

        [Fact]
        public void SourceMapFallsBackWhenInvalid()
        {
            Assert.Equal(new[] { 0, 1, 2, 3 }, PaddleSlots.NormalizeSourceMap(null));
            Assert.Equal(new[] { 0, 1, 2, 3 }, PaddleSlots.NormalizeSourceMap(new[] { 0, 0, 1, 2 }));
            Assert.Equal(new[] { 0, 1, 2, 3 }, PaddleSlots.NormalizeSourceMap(new[] { 0, 1, 2, 9 }));
            Assert.Equal(new[] { 3, 2, 1, 0 }, PaddleSlots.NormalizeSourceMap(new[] { 3, 2, 1, 0 }));
        }

        [Fact]
        public void MissingBlockMeansAllOffAndRoundTripsThroughBuild()
        {
            Assert.All(PaddleMappings.Resolve(new InputProfile()), s => Assert.False(s.IsActive));

            var settings = new[]
            {
                new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.MouseButton, Input = MouseInputNames.X1 },
                new PaddleSetting { Enabled = false, Kind = PaddleTargetKind.Key, Input = "Space" },
                PaddleSetting.Off,
                new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.PadButton, Input = ControllerEngine.ActionRsR3 }
            };
            var back = PaddleMappings.Resolve(new InputProfile { Paddles = PaddleMappings.Build(settings) });
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(settings[i].Enabled, back[i].Enabled);
                Assert.Equal(settings[i].Kind, back[i].Kind);
                Assert.Equal(settings[i].Input, back[i].Input);
            }
        }

        [Fact]
        public void TargetsComeFromThreeDistinctGroups()
        {
            Assert.Equal(18, PaddleMappings.PadButtonTargets.Length);
            Assert.DoesNotContain(ControllerEngine.ActionLeftStickUp, PaddleMappings.PadButtonTargets);
            Assert.Equal("Space", PaddleMappings.Canonical(PaddleTargetKind.Key, "space"));
            Assert.Null(PaddleMappings.Canonical(PaddleTargetKind.Key, MouseInputNames.Left));
            Assert.Equal(MouseInputNames.X2, PaddleMappings.Canonical(PaddleTargetKind.MouseButton, "mouse x2"));
            Assert.Null(PaddleMappings.Canonical(PaddleTargetKind.MouseButton, MouseInputNames.WheelUp));   // the wheel cannot be held
            Assert.Equal(ControllerEngine.ActionTouchpad, PaddleMappings.Canonical(PaddleTargetKind.PadButton, "touchpad click"));
        }

        [Fact]
        public void TryAssignRefusesDuplicatesAndInvalidTargets()
        {
            PaddleSetting[] s = Enumerable.Repeat(PaddleSetting.Off, 4).ToArray();
            Assert.True(PaddleMappings.TryAssign(s, 0, true, PaddleTargetKind.Key, "R", out _));
            Assert.False(PaddleMappings.TryAssign(s, 1, true, PaddleTargetKind.Key, "r", out string err));
            Assert.Contains("Left Upper Paddle", err);
            Assert.False(s[1].HasTarget);                                           // unchanged
            Assert.True(PaddleMappings.TryAssign(s, 1, false, PaddleTargetKind.Key, "R", out _));   // a disabled paddle may hold the same target
            Assert.True(PaddleMappings.TryAssign(s, 0, true, PaddleTargetKind.None, "None", out _));
            Assert.True(PaddleMappings.TryAssign(s, 2, true, PaddleTargetKind.Key, "R", out _));    // freed by clearing slot 0
            Assert.False(PaddleMappings.TryAssign(s, 3, true, PaddleTargetKind.Key, "Banana", out _));
            Assert.False(PaddleMappings.TryAssign(s, 7, true, PaddleTargetKind.Key, "R", out _));
        }
    }

    public class PaddleCapabilityTests
    {
        [Theory]
        [InlineData(0x045E, 0x02E3, 4)]
        [InlineData(0x045E, 0x0B00, 4)]
        [InlineData(0x045E, 0x0B22, 4)]
        [InlineData(0x045E, 0x028E, 0)]    // Xbox 360 pad: no paddles
        [InlineData(0x054C, 0x0CE6, 0)]    // DualSense: none exposed
        [InlineData(0, 0, 0)]
        public void OnlyKnownControllersAreAssumedToHavePaddles(int vid, int pid, int count) =>
            Assert.Equal(count, PaddleCaps.KnownPaddleCount(vid, pid));

        [Fact]
        public void AvailabilityFollowsTheDeviceAndWhatWasSeenPressed()
        {
            var elite = new PaddleReading(true, 0, "Elite", 0x045E, 0x0B00);
            Assert.Equal(0xF, PaddleCaps.AvailableMask(elite, 0));

            var other = new PaddleReading(true, 0, "Pad", 0x1234, 0x5678);
            Assert.Equal(0, PaddleCaps.AvailableMask(other, 0));
            Assert.Equal(0b0100, PaddleCaps.AvailableMask(other, 0b0100));         // seen pressed: available

            Assert.Equal(0, PaddleCaps.AvailableMask(PaddleReading.None, 0xF));     // not connected: nothing
        }

        [Fact]
        public void SlotAvailabilityUsesTheSourceMap()
        {
            int[] map = { 3, 2, 1, 0 };
            Assert.True(PaddleCaps.SlotAvailable(0b0001, map, 3));    // slot 3 is fed by hardware paddle 0
            Assert.False(PaddleCaps.SlotAvailable(0b0001, map, 0));
            Assert.False(PaddleCaps.SlotAvailable(0xF, map, 9));
        }
    }

    public class PaddleProcessorTests
    {
        private static PaddleReading Pad(int mask) => new(true, mask, "Elite", 0x045E, 0x0B00);

        private static PaddleSetting Key(string k) => new() { Enabled = true, Kind = PaddleTargetKind.Key, Input = k };
        private static PaddleSetting Mouse(string m) => new() { Enabled = true, Kind = PaddleTargetKind.MouseButton, Input = m };
        private static PaddleSetting Button(string a) => new() { Enabled = true, Kind = PaddleTargetKind.PadButton, Input = a };

        private static (PaddleProcessor P, FakeInjector I) Make()
        {
            var inj = new FakeInjector();
            var p = new PaddleProcessor { Injector = inj };
            p.Update(Pad(0), null, null);   // the controller is connected with nothing pressed (a paddle already held at connect time is ignored on purpose)
            return (p, inj);
        }

        private static int Bit(string action) => 1 << Array.IndexOf(ControllerEngine.BindingActions, action);

        [Fact]
        public void APressAndReleaseAreOneEdgeEach()
        {
            var (p, inj) = Make();
            var set = new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            p.Update(Pad(0), set, null); p.Flush();
            for (int i = 0; i < 5; i++) { p.Update(Pad(1), set, null); p.Flush(); }     // held for many ticks
            p.Update(Pad(0), set, null); p.Flush();
            Assert.Equal(new[] { "+R", "-R" }, inj.Log);                                   // no repeats
        }

        [Fact]
        public void PadButtonTargetsBecomeBitsWhileHeld()
        {
            var (p, _) = Make();
            var set = new[] { Button(ControllerEngine.ActionACross), Button(ControllerEngine.ActionRsR3), PaddleSetting.Off, PaddleSetting.Off };
            Assert.Equal(0, p.Update(Pad(0), set, null));
            Assert.Equal(Bit(ControllerEngine.ActionACross), p.Update(Pad(0b01), set, null));
            Assert.Equal(Bit(ControllerEngine.ActionACross) | Bit(ControllerEngine.ActionRsR3), p.Update(Pad(0b11), set, null));
            Assert.Equal(Bit(ControllerEngine.ActionRsR3), p.Update(Pad(0b10), set, null));
            Assert.Equal(0, p.Update(Pad(0), set, null));
        }

        [Fact]
        public void SimultaneousPaddlesAreIndependent()
        {
            var (p, inj) = Make();
            var set = new[] { Key("Q"), Key("E"), Mouse(MouseInputNames.X1), Button(ControllerEngine.ActionBCircle) };
            int mask = p.Update(Pad(0b1111), set, null); p.Flush();
            Assert.Equal(Bit(ControllerEngine.ActionBCircle), mask);
            Assert.Equal(new[] { "+Q", "+E", "+Mouse X1" }, inj.Log);
            inj.Log.Clear();
            p.Update(Pad(0b1010), set, null); p.Flush();                    // release slots 0 and 2 only
            Assert.Equal(new[] { "-Q", "-Mouse X1" }, inj.Log);
        }

        [Fact]
        public void DisabledOrUnmappedPaddlesDoNothing()
        {
            var (p, inj) = Make();
            var off = new PaddleSetting { Enabled = false, Kind = PaddleTargetKind.Key, Input = "R" };
            var noTarget = new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.None, Input = "None" };
            var set = new[] { off, noTarget, PaddleSetting.Off, PaddleSetting.Off };
            Assert.Equal(0, p.Update(Pad(0b1111), set, null)); p.Flush();
            Assert.Empty(inj.Log);
        }

        [Fact]
        public void DisablingWhileHeldReleasesAndDoesNotStickOrRetrigger()
        {
            var (p, inj) = Make();
            var on = new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            var off = new[] { PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            p.Update(Pad(1), on, null); p.Flush();
            p.Update(Pad(1), off, null); p.Flush();                          // switched off mid-hold
            p.Update(Pad(1), on, null); p.Flush();                           // back on while still held: needs a fresh press
            Assert.Equal(new[] { "+R", "-R" }, inj.Log);
            p.Update(Pad(0), on, null); p.Update(Pad(1), on, null); p.Flush();
            Assert.Equal(new[] { "+R", "-R", "+R" }, inj.Log);
        }

        [Fact]
        public void ChangingTheTargetWhileHeldReleasesTheOldOne()
        {
            var (p, inj) = Make();
            p.Update(Pad(1), new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off }, null); p.Flush();
            p.Update(Pad(1), new[] { Key("E"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off }, null); p.Flush();
            Assert.Equal(new[] { "+R", "-R" }, inj.Log);                     // E waits for the next press
            Assert.False(p.IsInjected("R"));
        }

        [Fact]
        public void DisconnectReleasesEverythingAndReconnectIgnoresAHeldPaddle()
        {
            var (p, inj) = Make();
            var set = new[] { Key("R"), Button(ControllerEngine.ActionACross), PaddleSetting.Off, PaddleSetting.Off };
            p.Update(Pad(0b11), set, null); p.Flush();
            Assert.True(p.IsInjected("R"));

            Assert.Equal(0, p.Update(PaddleReading.None, set, null)); p.Flush();   // unplugged while held
            Assert.Equal(new[] { "+R", "-R" }, inj.Log);
            Assert.False(p.IsInjected("R"));
            Assert.Equal(0, p.LastPadMask);

            inj.Log.Clear();
            Assert.Equal(0, p.Update(Pad(0b11), set, null)); p.Flush();           // reconnected with both still held
            Assert.Empty(inj.Log);
            Assert.Equal(0, p.Update(Pad(0b10), set, null));                      // slot 0 released: still ignored slot 1
            Assert.Equal(0, p.Update(Pad(0b00), set, null));
            Assert.Equal(Bit(ControllerEngine.ActionACross), p.Update(Pad(0b10), set, null));   // fresh press counts
        }

        [Fact]
        public void ReleaseAllClearsEverythingForShutdown()
        {
            var (p, inj) = Make();
            var set = new[] { Key("R"), Mouse(MouseInputNames.Left), Button(ControllerEngine.ActionACross), PaddleSetting.Off };
            p.Update(Pad(0b0111), set, null); p.Flush();
            p.ReleaseAll(); p.Flush();
            Assert.Equal(new[] { "+R", "+Mouse Left", "-R", "-Mouse Left" }, inj.Log);
            Assert.Equal(0, p.LastPadMask);
            Assert.Equal(0, p.LastHeldSlots);
            Assert.False(p.IsInjected("R"));
            Assert.False(p.IsInjected("Mouse Left"));
        }

        [Fact]
        public void SharedTargetIsReleasedOnlyAfterTheLastPaddleLetsGo()
        {
            var (p, inj) = Make();
            var set = new[] { Key("R"), Key("R"), PaddleSetting.Off, PaddleSetting.Off };   // bypasses the UI's duplicate check
            p.Update(Pad(0b11), set, null); p.Flush();
            p.Update(Pad(0b10), set, null); p.Flush();
            Assert.Equal(new[] { "+R" }, inj.Log);
            p.Update(Pad(0b00), set, null); p.Flush();
            Assert.Equal(new[] { "+R", "-R" }, inj.Log);
        }

        [Fact]
        public void InjectedKeysAreRecognizedWhileHeldSoTheEngineIgnoresTheirEcho()
        {
            var (p, _) = Make();
            var set = new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            Assert.False(p.IsInjected("R"));
            p.Update(Pad(1), set, null);
            Assert.True(p.IsInjected("R"));
            p.Flush();
            Assert.True(p.IsInjected("R"));
            p.Update(Pad(0), set, null); p.Flush();
            Assert.False(p.IsInjected("R"));
            Assert.False(p.IsInjected(null));
        }

        [Fact]
        public void SourceMapRoutesHardwarePaddlesToSlots()
        {
            var (p, inj) = Make();
            var set = new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            int[] map = { 2, 1, 0, 3 };                                       // slot 0 is fed by hardware paddle 3 (index 2)
            p.Update(Pad(0b0001), set, map); p.Flush();
            Assert.Empty(inj.Log);                                             // hardware paddle 1 is not slot 0 any more
            p.Update(Pad(0b0100), set, map); p.Flush();
            Assert.Equal(new[] { "+R" }, inj.Log);
        }

        [Fact]
        public void AFailingInjectorNeverBreaksTheEngineTick()
        {
            var p = new PaddleProcessor { Injector = new ThrowingInjector() };
            var set = new[] { Key("R"), PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off };
            p.Update(Pad(1), set, null);
            p.Flush();                                                         // must not throw
            p.Update(Pad(0), set, null);
            p.Flush();
            Assert.False(p.IsInjected("R"));
        }

        private sealed class ThrowingInjector : IInputInjector
        {
            public void Send(InjectedInput input, bool down) => throw new InvalidOperationException("no");
        }
    }

    public class ControllerLayoutTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryControlAndPaddleHasASlot(bool xbox)
        {
            PadFamily family = xbox ? PadFamily.Xbox360 : PadFamily.DualShock4;
            var slots = ControllerLayout.For(family);
            var ids = slots.Select(s => s.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            foreach (string action in ControllerEngine.BindingActions.Where(a => family == PadFamily.DualShock4 || a != ControllerEngine.ActionTouchpad))
                Assert.Contains(action, ids);
            Assert.Contains(ControllerLayout.RightStickId, ids);
            for (int i = 0; i < PaddleSlots.Count; i++) Assert.Contains(PaddleSlots.Id(i), ids);
        }

        [Fact]
        public void LayoutsDifferBetweenControllers()
        {
            var ds4 = ControllerLayout.For(PadFamily.DualShock4);
            var xbox = ControllerLayout.For(PadFamily.Xbox360);
            Assert.Contains(ds4, s => s.Id == ControllerEngine.ActionTouchpad);
            Assert.DoesNotContain(xbox, s => s.Id == ControllerEngine.ActionTouchpad);
            LayoutSlot a = ControllerLayout.Find(PadFamily.DualShock4, ControllerEngine.ActionLsL3);
            LayoutSlot b = ControllerLayout.Find(PadFamily.Xbox360, ControllerEngine.ActionLsL3);
            Assert.NotEqual((a.MX, a.MY), (b.MX, b.MY));
            Assert.Equal("Cross", ControllerLayout.Find(PadFamily.DualShock4, ControllerEngine.ActionACross).Title);
            Assert.Equal("A", ControllerLayout.Find(PadFamily.Xbox360, ControllerEngine.ActionACross).Title);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EverythingFitsInsideTheDiagram(bool xbox)
        {
            PadFamily family = xbox ? PadFamily.Xbox360 : PadFamily.DualShock4;
            foreach (LayoutSlot s in ControllerLayout.For(family))
            {
                Assert.InRange(s.MX - s.MW / 2, 0, ControllerLayout.Width);
                Assert.InRange(s.MX + s.MW / 2, 0, ControllerLayout.Width);
                Assert.InRange(s.MY - s.MH / 2, 0, ControllerLayout.Height);
                Assert.InRange(s.MY + s.MH / 2, 0, ControllerLayout.Height);
                var c = s.Chip;
                Assert.InRange(c.X, 0, ControllerLayout.Width - c.W + 0.001);
                Assert.InRange(c.Y, 0, ControllerLayout.Height - c.H + 0.001);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ChipsNeverOverlapEachOther(bool xbox)
        {
            PadFamily family = xbox ? PadFamily.Xbox360 : PadFamily.DualShock4;
            var chips = ControllerLayout.For(family).Select(s => s.Chip).ToList();
            for (int i = 0; i < chips.Count; i++)
                for (int j = i + 1; j < chips.Count; j++)
                {
                    bool overlap = chips[i].X < chips[j].X + chips[j].W && chips[j].X < chips[i].X + chips[i].W
                                && chips[i].Y < chips[j].Y + chips[j].H && chips[j].Y < chips[i].Y + chips[i].H;
                    Assert.False(overlap, $"chips {i} and {j} overlap");
                }
        }

        [Fact]
        public void ClickingAMarkerOrChipSelectsThatControl()
        {
            foreach (PadFamily family in new[] { PadFamily.DualShock4, PadFamily.Xbox360 })
                foreach (LayoutSlot s in ControllerLayout.For(family))
                {
                    var c = s.Chip;
                    Assert.Equal(s.Id, ControllerLayout.HitTest(family, c.X + c.W / 2, c.Y + c.H / 2).Id);       // its chip
                }

            LayoutSlot cross = ControllerLayout.Find(PadFamily.DualShock4, ControllerEngine.ActionACross);
            Assert.Equal(cross.Id, ControllerLayout.HitTest(PadFamily.DualShock4, cross.MX, cross.MY).Id);        // its marker
            LayoutSlot paddle = ControllerLayout.Find(PadFamily.DualShock4, PaddleSlots.Id(1));
            Assert.Equal(paddle.Id, ControllerLayout.HitTest(PadFamily.DualShock4, paddle.MX, paddle.MY).Id);
            Assert.Null(ControllerLayout.HitTest(PadFamily.DualShock4, 5, ControllerLayout.Height - 5));
        }

        [Fact]
        public void TheSmallerControlWinsOverlappingMarkers()
        {
            // a left-stick direction sits on top of the L3 click ring; the right stick (mouse) on top of R3
            LayoutSlot up = ControllerLayout.Find(PadFamily.DualShock4, ControllerEngine.ActionLeftStickUp);
            Assert.Equal(up.Id, ControllerLayout.HitTest(PadFamily.DualShock4, up.MX, up.MY).Id);
            LayoutSlot right = ControllerLayout.Find(PadFamily.DualShock4, ControllerLayout.RightStickId);
            Assert.Equal(right.Id, ControllerLayout.HitTest(PadFamily.DualShock4, right.MX, right.MY).Id);
            LayoutSlot r3 = ControllerLayout.Find(PadFamily.DualShock4, ControllerEngine.ActionRsR3);
            Assert.Equal(r3.Id, ControllerLayout.HitTest(PadFamily.DualShock4, r3.MX + 27, r3.MY).Id);             // ring outside the stick marker
        }
    }
}
