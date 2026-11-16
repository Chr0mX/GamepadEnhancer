using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    /// <summary>The default keyboard + mouse layout, exactly as specified, and nothing more.</summary>
    public class DefaultMappingTests
    {
        private static readonly Dictionary<string, string> Expected = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A / Cross"] = "Space",
            ["B / Circle"] = "Ctrl",
            ["X / Square"] = "R",
            ["Y / Triangle"] = "E",
            ["LB / L1"] = "Q",
            ["RB / R1"] = "Mouse X1",
            ["LT / L2"] = "Mouse Right",
            ["RT / R2"] = "Mouse Left",
            ["LS / L3"] = "Shift",
            ["RS / R3"] = "Mouse Middle",
            ["DPad Up"] = "1",
            ["DPad Down"] = "2",
            ["DPad Left"] = "3",
            ["DPad Right"] = "4",
            ["Start / Options"] = "Escape",
            ["Back / Share"] = "Tab",
            ["Guide / PS"] = "Home",
            ["Touchpad Click"] = "G",
            ["Left Stick Up"] = "W",
            ["Left Stick Down"] = "S",
            ["Left Stick Left"] = "A",
            ["Left Stick Right"] = "D"
        };

        [Fact]
        public void ExpectedTableCoversEveryAction()
        {
            Assert.Equal(ControllerEngine.BindingActions.OrderBy(x => x), Expected.Keys.OrderBy(x => x));
        }

        [Fact]
        public void ShippedDefaultIsExactlyTheRequestedMapping()
        {
            InputProfile d = DefaultProfile.Create();
            Assert.Equal("PS4", d.ControllerType);
            Assert.True(d.MouseLookEnabled);     // right stick = mouse movement
            var warnings = new List<string>();
            Dictionary<string, MappingEntry> map = ProfileMappings.Resolve(d, warnings);
            Assert.Empty(warnings);
            foreach (var pair in Expected)
            {
                Assert.Equal(pair.Value, map[pair.Key].Input);
                Assert.False(map[pair.Key].Turbo);
            }
            Assert.Equal(Expected.Count, map.Count);
        }

        [Fact]
        public void ShippedKindsMatchTheInputs()
        {
            InputProfile d = DefaultProfile.Create();
            Assert.Equal("MouseButton", d.Mappings["RB / R1"].Kind);
            Assert.Equal("MouseButton", d.Mappings["RS / R3"].Kind);
            Assert.Equal("Keyboard", d.Mappings["A / Cross"].Kind);
            Assert.Equal("Keyboard", d.Mappings["Left Stick Up"].Kind);
        }

        [Fact]
        public void LeftStickDefaultsAreKeyboardOnly()
        {
            foreach (string a in new[] { "Left Stick Up", "Left Stick Down", "Left Stick Left", "Left Stick Right" })
                Assert.True(InputChoices.IsStickAction(a));
            Assert.False(InputChoices.IsAllowed("Left Stick Up", MouseInputNames.Left));
            Assert.True(InputChoices.IsAllowed("Left Stick Up", "W"));
            Assert.True(InputChoices.IsAllowed("A / Cross", MouseInputNames.Left));
        }
    }

    public class ApexHintsTests
    {
        [Theory]
        [InlineData(ControllerEngine.ActionACross, "Jump")]
        [InlineData(ControllerEngine.ActionBCircle, "Crouch")]
        [InlineData(ControllerEngine.ActionXSquare, "Reload")]
        [InlineData(ControllerEngine.ActionYTriangle, "Cycle Weapon")]
        [InlineData(ControllerEngine.ActionLbL1, "Tactical")]
        [InlineData(ControllerEngine.ActionRbR1, "Ping")]
        [InlineData(ControllerEngine.ActionLtL2, "Aim Down Sight")]
        [InlineData(ControllerEngine.ActionRtR2, "Attack")]
        [InlineData(ControllerEngine.ActionLsL3, "Sprint")]
        [InlineData(ControllerEngine.ActionRsR3, "Melee")]
        [InlineData(ControllerEngine.ActionDPadUp, "Health")]
        [InlineData(ControllerEngine.ActionDPadRight, "Fire Mode")]
        [InlineData(ControllerEngine.ActionDPadDown, "Grenade")]
        [InlineData(ControllerEngine.ActionDPadLeft, "Extra Character Action")]
        [InlineData(ControllerEngine.ActionBackShare, "Map")]
        [InlineData(ControllerEngine.ActionStartOptions, "Inventory")]
        public void ButtonsShowTheirApexAction(string action, string expectedPart) =>
            Assert.Contains(expectedPart, ApexHints.For(action));

        [Fact]
        public void ActionsNotOnThePresetHaveNoHint()
        {
            foreach (string a in new[] { ControllerEngine.ActionGuidePs, ControllerEngine.ActionTouchpad,
                ControllerEngine.ActionLeftStickUp, ControllerEngine.ActionLeftStickDown,
                ControllerEngine.ActionLeftStickLeft, ControllerEngine.ActionLeftStickRight })
                Assert.Null(ApexHints.For(a));
        }

        [Fact]
        public void EveryHintBelongsToARealAction()
        {
            int withHint = ControllerEngine.BindingActions.Count(a => ApexHints.For(a) != null);
            Assert.Equal(16, withHint);
        }
    }

    public class LeftStickKeysTests
    {
        private static int Bit(string action) => 1 << Array.IndexOf(ControllerEngine.BindingActions, action);

        [Fact]
        public void ActionIndexesMatchTheirNames()
        {
            string[] a = ControllerEngine.BindingActions;
            Assert.Equal(ControllerEngine.ActionLeftStickUp, a[PadStateMath.ActionLeftUp]);
            Assert.Equal(ControllerEngine.ActionLeftStickDown, a[PadStateMath.ActionLeftDown]);
            Assert.Equal(ControllerEngine.ActionLeftStickLeft, a[PadStateMath.ActionLeftLeft]);
            Assert.Equal(ControllerEngine.ActionLeftStickRight, a[PadStateMath.ActionLeftRight]);
        }

        [Fact]
        public void SingleKeysGiveFullDeflection()
        {
            Assert.Equal((0.0, 1.0), PadStateMath.LeftFromMask(Bit(ControllerEngine.ActionLeftStickUp)));
            Assert.Equal((0.0, -1.0), PadStateMath.LeftFromMask(Bit(ControllerEngine.ActionLeftStickDown)));
            Assert.Equal((-1.0, 0.0), PadStateMath.LeftFromMask(Bit(ControllerEngine.ActionLeftStickLeft)));
            Assert.Equal((1.0, 0.0), PadStateMath.LeftFromMask(Bit(ControllerEngine.ActionLeftStickRight)));
            Assert.Equal((0.0, 0.0), PadStateMath.LeftFromMask(0));
        }

        [Fact]
        public void OppositeKeysCancel()
        {
            int mask = Bit(ControllerEngine.ActionLeftStickLeft) | Bit(ControllerEngine.ActionLeftStickRight);
            Assert.Equal((0.0, 0.0), PadStateMath.LeftFromMask(mask));
        }

        [Fact]
        public void DiagonalsAreNormalizedToTheUnitCircle()
        {
            int mask = Bit(ControllerEngine.ActionLeftStickUp) | Bit(ControllerEngine.ActionLeftStickRight);
            var (x, y) = PadStateMath.LeftFromMask(mask);
            Assert.True(x > 0 && y > 0);
            Assert.Equal(1.0, Math.Sqrt(x * x + y * y), 6);
        }

        [Fact]
        public void OtherButtonsDoNotMoveTheStick()
        {
            Assert.Equal((0.0, 0.0), PadStateMath.LeftFromMask(Bit(ControllerEngine.ActionACross) | Bit(ControllerEngine.ActionTouchpad)));
        }
    }

    public class ProfileMappingsTests
    {
        [Fact]
        public void MappingsRoundTripThroughJson()
        {
            var rows = new[] { ("A / Cross", "Space", true, 20), ("RB / R1", "Mouse X1", false, 12) };
            var p = new InputProfile { Name = "T", Mappings = ProfileMappings.Build(rows) };
            InputProfile back = JsonSerializer.Deserialize<InputProfile>(JsonSerializer.Serialize(p));
            var map = ProfileMappings.Resolve(back);
            Assert.Equal("Space", map["A / Cross"].Input);
            Assert.True(map["A / Cross"].Turbo);
            Assert.Equal(20, map["A / Cross"].TurboHz);
            Assert.Equal("Mouse X1", map["RB / R1"].Input);
            Assert.Equal("None", map["Guide / PS"].Input);
        }

        [Fact]
        public void SavedProfileWritesMappingsNotTheLegacyDictionaries()
        {
            string json = JsonSerializer.Serialize(new InputProfile { Mappings = ProfileMappings.Build(new[] { ("A / Cross", "Space", false, 12) }) });
            Assert.Contains("\"Mappings\"", json);
            Assert.DoesNotContain("KeyBindings", json);
            Assert.DoesNotContain("TurboEnabled", json);
            Assert.Contains("\"Kind\":\"Keyboard\"", json);
        }

        [Fact]
        public void LegacyDictionariesStillResolve()
        {
            var p = new InputProfile
            {
                KeyBindings = new Dictionary<string, string> { ["A / Cross"] = "Space", ["RT / R2"] = "Mouse Left" },
                TurboEnabled = new Dictionary<string, bool> { ["A / Cross"] = true },
                TurboHz = new Dictionary<string, int> { ["A / Cross"] = 99 }
            };
            var map = ProfileMappings.Resolve(p);
            Assert.Equal("Space", map["A / Cross"].Input);
            Assert.True(map["A / Cross"].Turbo);
            Assert.Equal(30, map["A / Cross"].TurboHz);   // clamped
            Assert.Equal("Mouse Left", map["RT / R2"].Input);
            Assert.Equal("None", map["B / Circle"].Input);
        }

        [Fact]
        public void KindInTheFileIsIgnoredAndRederived()
        {
            const string json = "{\"Mappings\":{\"A / Cross\":{\"Input\":\"Space\",\"Kind\":\"MouseButton\"}}}";
            var map = ProfileMappings.Resolve(JsonSerializer.Deserialize<InputProfile>(json));
            Assert.Equal("Keyboard", map["A / Cross"].Kind);
        }

        [Fact]
        public void UnknownAndDisallowedInputsAreClearedWithAWarning()
        {
            const string json = "{\"Mappings\":{\"A / Cross\":{\"Input\":\"Banana\"},\"Left Stick Up\":{\"Input\":\"Mouse Left\"},\"B / Circle\":{\"Input\":\"ctrl\"}}}";
            var warnings = new List<string>();
            var map = ProfileMappings.Resolve(JsonSerializer.Deserialize<InputProfile>(json), warnings);
            Assert.Equal("None", map["A / Cross"].Input);
            Assert.Equal("None", map["Left Stick Up"].Input);
            Assert.Equal("Ctrl", map["B / Circle"].Input);    // case-insensitive, canonical casing
            Assert.Equal(2, warnings.Count);
        }
    }

    public class ProfileImportTests
    {
        [Fact]
        public void RejectsEmptyBadAndOversizedFiles()
        {
            Assert.Null(ProfileImport.Parse("", out string e1, out _)); Assert.NotNull(e1);
            Assert.Null(ProfileImport.Parse("not json", out string e2, out _)); Assert.NotNull(e2);
            Assert.Null(ProfileImport.Parse(new string(' ', ProfileImport.MaxBytes + 1), out string e3, out _)); Assert.NotNull(e3);
        }

        [Fact]
        public void NormalizesAndReportsWhatItCleared()
        {
            const string json = "{\"Name\":\"\",\"ControllerType\":\"Weird\",\"MouseSensitivityX\":9999,\"MouseReleaseMs\":-5," +
                                "\"Mappings\":{\"A / Cross\":{\"Input\":\"space\"},\"B / Circle\":{\"Input\":\"Nope\"},\"Not An Action\":{\"Input\":\"A\"}}}";
            InputProfile p = ProfileImport.Parse(json, out string error, out List<string> warnings);
            Assert.Null(error);
            Assert.Equal("Imported", p.Name);
            Assert.Equal("PS4", p.ControllerType);
            Assert.Equal(100.0, p.MouseSensitivityX);
            Assert.Equal(0, p.MouseReleaseMs);
            Assert.Equal("Space", p.Mappings["A / Cross"].Input);
            Assert.Equal("None", p.Mappings["B / Circle"].Input);
            Assert.Equal(ControllerEngine.BindingActions.Length, p.Mappings.Count);
            Assert.Contains(warnings, w => w.Contains("unknown action"));
            Assert.Contains(warnings, w => w.Contains("Nope"));
        }

        [Fact]
        public void OldFormatFilesAreConvertedToMappings()
        {
            InputProfile p = ProfileImport.Parse("{\"KeyBindings\":{\"A / Cross\":\"Space\"}}", out _, out _);
            Assert.Null(p.KeyBindings);
            Assert.Equal("Space", p.Mappings["A / Cross"].Input);
        }
    }

    public class DefaultUpgradeTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(DefaultProfile.MappingVersion, false)]
        [InlineData(DefaultProfile.MappingVersion + 1, false)]
        public void UpgradeRunsOnlyBelowTheShippedVersion(int stored, bool expected) =>
            Assert.Equal(expected, DefaultProfile.ShouldUpgradeDefault(stored));

        [Fact]
        public void BackupNeverOverwritesAnExistingBackup()
        {
            var existing = new HashSet<string>();
            Assert.Equal("Default.json.bak", DefaultProfile.NextBackupName(existing.Contains));
            existing.Add("Default.json.bak");
            Assert.Equal("Default.json.bak2", DefaultProfile.NextBackupName(existing.Contains));
            existing.Add("Default.json.bak2");
            Assert.Equal("Default.json.bak3", DefaultProfile.NextBackupName(existing.Contains));
        }

        [Fact]
        public void UpgradeReplacesTheFileOnceAndKeepsTheOldOne()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-upgrade-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "Default.json");
                System.IO.File.WriteAllText(path, "{\"Name\":\"Mine\"}");

                Assert.True(DefaultProfile.PrepareStartup(dir, 0));
                Assert.Equal("{\"Name\":\"Mine\"}", System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "Default.json.bak")));
                Assert.Contains("\"Space\"", System.IO.File.ReadAllText(path));

                System.IO.File.WriteAllText(path, "{\"Name\":\"EditedAgain\"}");
                Assert.False(DefaultProfile.PrepareStartup(dir, DefaultProfile.MappingVersion));   // already upgraded: left alone
                Assert.Equal("{\"Name\":\"EditedAgain\"}", System.IO.File.ReadAllText(path));
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        }
    }
}
