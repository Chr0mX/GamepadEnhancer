using System;
using System.Collections.Generic;
using System.Linq;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    /// <summary>The canonical layers: physical ids, controller layouts (labels), game actions, profile resolution.</summary>
    public class CanonicalIdTests
    {
        [Fact]
        public void PhysicalIdsAreUniqueUpperSnakeCaseAndNameNoBrand()
        {
            var all = Inputs.Buttons.Concat(Inputs.Paddles).Append(Inputs.RightStickMove).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
            string[] brands = { "CROSS", "CIRCLE", "SQUARE", "TRIANGLE", "XBOX", "PS4", "L1", "R1", "L2", "R2", "L3", "R3", "SHARE", "OPTIONS", "GUIDE" };
            foreach (string id in all)
            {
                Assert.Matches("^[A-Z][A-Z_]*$", id);
                Assert.DoesNotContain(id.Split('_'), part => brands.Contains(part));
            }
        }

        [Fact]
        public void EngineBitOrderIsUnchanged()
        {
            Assert.Equal(22, ControllerEngine.BindingActions.Length);
            Assert.Equal("FACE_SOUTH", ControllerEngine.BindingActions[0]);
            Assert.Equal("FACE_EAST", ControllerEngine.BindingActions[1]);
            Assert.Equal("LB", ControllerEngine.BindingActions[4]);
            Assert.Equal("SELECT", ControllerEngine.BindingActions[8]);
        }

        [Fact]
        public void EveryLegacyIdMapsToACanonicalOne()
        {
            Assert.Equal("FACE_SOUTH", LegacyIds.ToCanonical("A / Cross"));
            Assert.Equal("SELECT", LegacyIds.ToCanonical("Back / Share"));
            Assert.Equal("HOME", LegacyIds.ToCanonical("Guide / PS"));
            Assert.Equal("PADDLE_LEFT_UPPER", LegacyIds.ToCanonical("LeftUpper"));
            foreach (string action in ControllerEngine.BindingActions) Assert.Contains(LegacyIds.All, k => LegacyIds.ToCanonical(k) == action);
        }
    }

    public class ControllerLayoutTests
    {
        [Fact]
        public void TheSameCanonicalIdShowsADifferentLabelPerController()
        {
            ControllerLayoutDef ps = ControllerLayouts.Get("dualshock4"), xb = ControllerLayouts.Get("xbox360");
            Assert.Equal("Cross", ps.Label(Inputs.FaceSouth));
            Assert.Equal("A", xb.Label(Inputs.FaceSouth));
            Assert.Equal("L1", ps.Label(Inputs.Lb));
            Assert.Equal("LB", xb.Label(Inputs.Lb));
            Assert.Equal("Share", ps.Label(Inputs.Select));
            Assert.Equal("View", xb.Label(Inputs.Select));
            Assert.False(xb.Supports(Inputs.Touchpad));
            Assert.True(ps.Supports(Inputs.Touchpad));
        }

        [Fact]
        public void UnlabelledInputsFallBackToNeutralNames()
        {
            ControllerLayoutDef ps = ControllerLayouts.Get("dualshock4");
            Assert.Equal("Left Upper Paddle", ps.Label(Inputs.PaddleLeftUpper));
            Assert.Equal("D-Pad Up", ps.Label(Inputs.DpadUp));
            Assert.Equal("Left Stick Up", ps.Label(Inputs.LsUp));
        }

        [Fact]
        public void LayoutsAreFoundByIdOrAliasAndUnknownFallsBack()
        {
            Assert.Equal("xbox360", ControllerLayouts.Get("Xbox 360").Id);
            Assert.Equal("dualshock4", ControllerLayouts.Get("PS4").Id);
            Assert.Equal(ControllerLayouts.DefaultId, ControllerLayouts.Get("nonsense").Id);
            Assert.Equal(PadFamily.Xbox360, ControllerLayouts.Get("xbox360").Family);
        }

        [Fact]
        public void ADataFileAddsAControllerWithoutCode()
        {
            const string json = "{\"Id\":\"switchpro\",\"Name\":\"Switch\",\"Title\":\"Switch Pro Controller\",\"Device\":\"Xbox360\"," +
                                "\"Labels\":{\"FACE_SOUTH\":\"B\",\"FACE_EAST\":\"A\"}}";
            ControllerLayoutDef def = ControllerLayouts.Parse(json, out string error);
            Assert.Null(error);
            Assert.Equal("B", def.Label(Inputs.FaceSouth));
            Assert.Equal("Left Bumper", def.Label(Inputs.Lb));
        }

        [Fact]
        public void PadLabelsDelegateToTheLayouts()
        {
            Assert.Equal("Cross", PadLabels.For(PadFamily.DualShock4, 0));
            Assert.Equal("A", PadLabels.For(PadFamily.Xbox360, 0));
        }
    }

    public class GameActionTests
    {
        [Fact]
        public void ApexActionsResolveToPhysicalInputsThroughTheNativeLayout()
        {
            GameDefinition apex = GameCatalog.Get("apex");
            Assert.Equal("FACE_SOUTH", apex.ResolvePhysical("JUMP"));
            Assert.Equal("LB", apex.ResolvePhysical("tactical"));
            Assert.Equal("FACE_SOUTH", apex.ResolvePhysical("FACE_SOUTH"));   // a physical id passes through
            Assert.Null(apex.ResolvePhysical("ULTIMATE"));                     // a combo has no single input
            Assert.Null(apex.ResolvePhysical("NOT_AN_ACTION"));
            Assert.Equal("Reload", apex.ActionName("RELOAD"));
        }

        [Fact]
        public void ActionKeyedProfileResolvesOntoPhysicalInputs()
        {
            InputProfile p = DefaultProfile.CreateApex();
            Assert.Equal("apex", p.Game);
            var map = ProfileMappings.Resolve(p, null);
            Assert.Equal("Space", map["FACE_SOUTH"].Input);     // JUMP
            Assert.Equal("Q", map["LB"].Input);                 // TACTICAL
        }

        [Fact]
        public void PhysicalEntriesOverrideActionEntriesAndConflictsWarn()
        {
            var p = new InputProfile { Game = "apex", Mappings = new Dictionary<string, MappingEntry>() };
            p.Mappings["JUMP"] = new MappingEntry { Input = "Z", Kind = "Keyboard" };
            p.Mappings["FACE_SOUTH"] = new MappingEntry { Input = "X", Kind = "Keyboard" };
            var warnings = new List<string>();
            var map = ProfileMappings.Resolve(p, warnings);
            Assert.Equal("X", map["FACE_SOUTH"].Input);
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public void LegacyProfilesMigrateToCanonicalIds()
        {
            InputProfile p = ProfileImport.Parse("{\"Mappings\":{\"A / Cross\":{\"Input\":\"Space\"},\"Back / Share\":{\"Input\":\"Tab\"}},\"ControllerType\":\"Xbox 360\"}", out string error, out _);
            Assert.Null(error);
            var map = ProfileMappings.Resolve(p, null);
            Assert.Equal("Space", map["FACE_SOUTH"].Input);
            Assert.Equal("Tab", map["SELECT"].Input);
            Assert.Equal("xbox360", p.ControllerType);
            Assert.Equal(2, p.Schema);
        }

        [Fact]
        public void GameDataNeverContainsBrandedButtonNames()
        {
            foreach (GameDefinition g in GameCatalog.All)
                foreach (var preset in g.NativeLayouts.Values)
                    foreach (var b in preset.Values)
                        foreach (string input in b.Inputs) Assert.True(Inputs.IsButton(input), input);
        }
    }
}
