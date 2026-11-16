using System.Collections.Generic;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class BlockPolicyTests
    {
        private static BlockPolicy Make(bool active = true, int hotkey = 0x91, bool mouseMove = false,
            IEnumerable<int> keys = null, IEnumerable<string> mouse = null)
        {
            var p = new BlockPolicy();
            p.Update(new BlockSettings
            {
                Active = active,
                HotkeyVk = hotkey,
                BlockMouseMove = mouseMove,
                BlockedKeys = new HashSet<int>(keys ?? new[] { 0x57 }),   // W
                BlockedMouseInputs = new HashSet<string>(mouse ?? new[] { MouseInputNames.Left, MouseInputNames.WheelUp }, System.StringComparer.OrdinalIgnoreCase)
            });
            return p;
        }

        [Fact]
        public void MappedKeyIsBlockedAndUnmappedPasses()
        {
            var p = Make();
            Assert.True(p.DecideKey(0x57, true, false));
            Assert.False(p.DecideKey(0x41, true, false));   // A is not mapped
        }

        [Fact]
        public void InactivePolicyBlocksNothing()
        {
            var p = Make(active: false);
            Assert.False(p.DecideKey(0x57, true, false));
            Assert.False(p.DecideMouseButton(MouseInputNames.Left, true, false));
            Assert.False(p.DecideWheel(true, false));
            Assert.False(p.DecideMouseMove(false));
        }

        [Fact]
        public void InjectedEventsAreNeverBlocked()
        {
            var p = Make(mouseMove: true);
            Assert.False(p.DecideKey(0x57, true, true));
            Assert.False(p.DecideMouseButton(MouseInputNames.Left, true, true));
            Assert.False(p.DecideWheel(true, true));
            Assert.False(p.DecideMouseMove(true));
        }

        [Fact]
        public void HotkeyIsNeverBlockedEvenIfMapped()
        {
            var p = Make(keys: new[] { 0x91 });
            Assert.False(p.DecideKey(0x91, true, false));
            Assert.False(p.DecideKey(0x91, false, false));
        }

        [Fact]
        public void UpOfSwallowedDownIsSwallowedEvenAfterDeactivation()
        {
            var p = Make();
            Assert.True(p.DecideKey(0x57, true, false));
            p.Update(BlockSettings.Off);
            Assert.True(p.DecideKey(0x57, true, false));    // auto-repeat of the swallowed key stays swallowed
            Assert.True(p.DecideKey(0x57, false, false));   // so the target never sees an unmatched up
            Assert.False(p.DecideKey(0x57, false, false));  // pairing is consumed
        }

        [Fact]
        public void UpOfPassedDownIsPassedEvenAfterActivation()
        {
            var p = Make(active: false);
            Assert.False(p.DecideKey(0x57, true, false));   // pressed before blocking began
            p.Update(new BlockSettings { Active = true, BlockedKeys = new HashSet<int> { 0x57 } });
            Assert.False(p.DecideKey(0x57, false, false));  // release must reach the target, or the key would stick down
        }

        [Fact]
        public void AutoRepeatDownsStayBlocked()
        {
            var p = Make();
            Assert.True(p.DecideKey(0x57, true, false));
            Assert.True(p.DecideKey(0x57, true, false));
            Assert.True(p.DecideKey(0x57, true, false));
            Assert.True(p.DecideKey(0x57, false, false));
        }

        [Fact]
        public void MouseButtonPairingAndWheelAndMove()
        {
            var p = Make(mouseMove: true);
            Assert.True(p.DecideMouseButton(MouseInputNames.Left, true, false));
            Assert.True(p.DecideMouseButton(MouseInputNames.Left, false, false));
            Assert.False(p.DecideMouseButton(MouseInputNames.Right, true, false));
            Assert.True(p.DecideWheel(true, false));
            Assert.False(p.DecideWheel(false, false));      // Wheel Down is not mapped
            Assert.True(p.DecideMouseMove(false));
        }

        [Fact]
        public void ResetForgetsPairing()
        {
            var p = Make();
            p.DecideKey(0x57, true, false);
            p.Reset();
            Assert.False(p.DecideKey(0x57, false, false));
        }

        [Fact]
        public void FromBuildsBlockedSetFromBindingsOnly()
        {
            var engine = new EngineSettings
            {
                MouseLookEnabled = true,
                Bindings = new Dictionary<string, BindingSetting>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["A / Cross"] = new BindingSetting { Key = "Space" },
                    ["RT / R2"] = new BindingSetting { Key = "Mouse Left" },
                    ["LT / L2"] = new BindingSetting { Key = "Mouse Right" },
                    ["X / Square"] = new BindingSetting { Key = "Shift" },
                    ["B / Circle"] = new BindingSetting { Key = "None" }
                }
            };
            BlockSettings s = BlockSettings.From(engine, true, 0x91);
            Assert.True(s.Active);
            Assert.True(s.BlockMouseMove);
            Assert.Contains(0x20, s.BlockedKeys);
            Assert.Contains(0xA0, s.BlockedKeys);           // left shift as the hook reports it
            Assert.DoesNotContain(0x09, s.BlockedKeys);     // Tab is not bound to a pad action
            Assert.Contains("Mouse Left", s.BlockedMouseInputs);
            Assert.Contains("Mouse Right", s.BlockedMouseInputs);
            Assert.Equal(0x91, s.HotkeyVk);
        }
    }
}
