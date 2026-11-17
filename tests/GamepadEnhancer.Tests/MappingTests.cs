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
            ["FACE_SOUTH"] = "Space",
            ["FACE_EAST"] = "C",
            ["FACE_WEST"] = "R",
            ["FACE_NORTH"] = "Wheel Up",
            ["LB"] = "Q",
            ["RB"] = "G",
            ["LT"] = "Mouse Right",
            ["RT"] = "Mouse Left",
            ["LS_CLICK"] = "Shift",
            ["RS_CLICK"] = "V",
            ["DPAD_UP"] = "1",
            ["DPAD_DOWN"] = "2",
            ["DPAD_LEFT"] = "3",
            ["DPAD_RIGHT"] = "4",
            ["START"] = "Escape",
            ["SELECT"] = "F12",
            ["HOME"] = "None",
            ["TOUCHPAD"] = "M",
            ["LS_UP"] = "W",
            ["LS_DOWN"] = "S",
            ["LS_LEFT"] = "A",
            ["LS_RIGHT"] = "D"
        };

        [Fact]
        public void ExpectedTableCoversEveryAction()
        {
            Assert.Equal(ControllerEngine.BindingActions.OrderBy(x => x), Expected.Keys.OrderBy(x => x));
        }

        [Fact]
        public void BuiltInApexIsExactlyTheRequestedMapping()
        {
            InputProfile d = DefaultProfile.CreateApex();
            Assert.Equal("Apex Legends", d.Name);
            Assert.Equal("dualshock4", d.ControllerType);
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
        public void ApexKindsMatchTheInputs()
        {
            var map = ProfileMappings.Resolve(DefaultProfile.CreateApex(), null);
            Assert.Equal("MouseButton", map["LT"].Kind);
            Assert.Equal("MouseButton", map["RT"].Kind);
            Assert.Equal("MouseWheel", map["FACE_NORTH"].Kind);
            Assert.Equal("Keyboard", map["FACE_SOUTH"].Kind);
            Assert.Equal("Keyboard", map["LS_UP"].Kind);
        }

        private static readonly Dictionary<string, string> DefaultExpected = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FACE_SOUTH"] = "Space",
            ["FACE_EAST"] = "C",
            ["FACE_WEST"] = "R",
            ["FACE_NORTH"] = "Wheel Up",
            ["LB"] = "Q",
            ["RB"] = "G",
            ["LT"] = "Mouse Right",
            ["RT"] = "Mouse Left",
            ["LS_CLICK"] = "Shift",
            ["RS_CLICK"] = "V",
            ["DPAD_UP"] = "1",
            ["DPAD_DOWN"] = "2",
            ["DPAD_LEFT"] = "3",
            ["DPAD_RIGHT"] = "4",
            ["TOUCHPAD"] = "M",
            ["START"] = "Escape",
            ["SELECT"] = "F12",
            ["HOME"] = "None",
            ["LS_UP"] = "W",
            ["LS_DOWN"] = "S",
            ["LS_LEFT"] = "A",
            ["LS_RIGHT"] = "D"
        };

        [Fact]
        public void TheDefaultConfigIsThePs4ToKeyboardMouseTable()
        {
            InputProfile d = DefaultProfile.Create();
            Assert.Equal("Default", d.Name);
            Assert.Equal("dualshock4", d.ControllerType);
            Assert.True(d.MouseLookEnabled);                          // right stick = mouse movement
            var warnings = new List<string>();
            var map = ProfileMappings.Resolve(d, warnings);
            Assert.Empty(warnings);
            Assert.Equal(DefaultExpected.Count, map.Count);
            foreach (var pair in DefaultExpected) { Assert.Equal(pair.Value, map[pair.Key].Input); Assert.False(map[pair.Key].Turbo); }
            var keys = map.Values.Select(e => e.Input).Where(i => i != "None").ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());   // no key drives two pad buttons
        }

        [Fact]
        public void TheXboxDefaultIsThePs4DefaultWithViewAsBackAndNoShare()
        {
            GameDefinition xbox = GameCatalog.Get("default-xbox");
            Assert.Equal("Default (Xbox 360)", xbox.DisplayName);
            InputProfile x = GameProfiles.BuiltIn(xbox);
            Assert.Equal("xbox360", x.ControllerType);
            Assert.True(x.MouseLookEnabled);
            var warnings = new List<string>();
            var map = ProfileMappings.Resolve(x, warnings);
            Assert.Empty(warnings);
            foreach (var pair in DefaultExpected)
            {
                string want = pair.Key == "SELECT" ? "M" : pair.Key == "TOUCHPAD" ? "None" : pair.Value;
                Assert.Equal(want, map[pair.Key].Input);
            }
            var keys = map.Values.Select(e => e.Input).Where(i => i != "None").ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.False(GameProfiles.Equivalent(x, DefaultProfile.Create()));
        }

        [Fact]
        public void DefaultAndApexAreDifferentConfigs()
        {
            Assert.False(GameProfiles.Equivalent(DefaultProfile.Create(), DefaultProfile.CreateApex()));
            Assert.Equal("Apex Legends", GameProfiles.BuiltIn(GameCatalog.Get("apex")).Name);
            Assert.Equal("Default (PS4)", GameProfiles.BuiltIn(GameCatalog.Generic).Name);   // the profile carries the display name
        }

        [Fact]
        public void LeftStickDefaultsAreKeyboardOnly()
        {
            foreach (string a in new[] { "LS_UP", "LS_DOWN", "LS_LEFT", "LS_RIGHT" })
                Assert.True(InputChoices.IsStickAction(a));
            Assert.False(InputChoices.IsAllowed("LS_UP", MouseInputNames.Left));
            Assert.True(InputChoices.IsAllowed("LS_UP", "W"));
            Assert.True(InputChoices.IsAllowed("FACE_SOUTH", MouseInputNames.Left));
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
            var rows = new[] { ("FACE_SOUTH", "Space", true, 20), ("RB", "Mouse X1", false, 12) };
            var p = new InputProfile { Name = "T", Mappings = ProfileMappings.Build(rows) };
            InputProfile back = JsonSerializer.Deserialize<InputProfile>(JsonSerializer.Serialize(p));
            var map = ProfileMappings.Resolve(back);
            Assert.Equal("Space", map["FACE_SOUTH"].Input);
            Assert.True(map["FACE_SOUTH"].Turbo);
            Assert.Equal(20, map["FACE_SOUTH"].TurboHz);
            Assert.Equal("Mouse X1", map["RB"].Input);
            Assert.Equal("None", map["HOME"].Input);
        }

        [Fact]
        public void SavedProfileWritesMappingsNotTheLegacyDictionaries()
        {
            string json = JsonSerializer.Serialize(new InputProfile { Mappings = ProfileMappings.Build(new[] { ("FACE_SOUTH", "Space", false, 12) }) });
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
                KeyBindings = new Dictionary<string, string> { ["FACE_SOUTH"] = "Space", ["RT"] = "Mouse Left" },
                TurboEnabled = new Dictionary<string, bool> { ["FACE_SOUTH"] = true },
                TurboHz = new Dictionary<string, int> { ["FACE_SOUTH"] = 99 }
            };
            var map = ProfileMappings.Resolve(p);
            Assert.Equal("Space", map["FACE_SOUTH"].Input);
            Assert.True(map["FACE_SOUTH"].Turbo);
            Assert.Equal(30, map["FACE_SOUTH"].TurboHz);   // clamped
            Assert.Equal("Mouse Left", map["RT"].Input);
            Assert.Equal("None", map["FACE_EAST"].Input);
        }

        [Fact]
        public void KindInTheFileIsIgnoredAndRederived()
        {
            const string json = "{\"Mappings\":{\"A / Cross\":{\"Input\":\"Space\",\"Kind\":\"MouseButton\"}}}";
            var map = ProfileMappings.Resolve(JsonSerializer.Deserialize<InputProfile>(json));
            Assert.Equal("Keyboard", map["FACE_SOUTH"].Kind);
        }

        [Fact]
        public void UnknownAndDisallowedInputsAreClearedWithAWarning()
        {
            const string json = "{\"Mappings\":{\"A / Cross\":{\"Input\":\"Banana\"},\"Left Stick Up\":{\"Input\":\"Mouse Left\"},\"B / Circle\":{\"Input\":\"ctrl\"}}}";
            var warnings = new List<string>();
            var map = ProfileMappings.Resolve(JsonSerializer.Deserialize<InputProfile>(json), warnings);
            Assert.Equal("None", map["FACE_SOUTH"].Input);
            Assert.Equal("None", map["LS_UP"].Input);
            Assert.Equal("Ctrl", map["FACE_EAST"].Input);    // case-insensitive, canonical casing
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
            Assert.Equal("dualshock4", p.ControllerType);
            Assert.Equal(100.0, p.MouseSensitivityX);
            Assert.Equal(0, p.MouseReleaseMs);
            Assert.Equal("Space", p.Mappings["FACE_SOUTH"].Input);
            Assert.Equal("None", p.Mappings["FACE_EAST"].Input);
            Assert.Equal(ControllerEngine.BindingActions.Length, p.Mappings.Count);
            Assert.Contains(warnings, w => w.Contains("unknown action"));
            Assert.Contains(warnings, w => w.Contains("Nope"));
        }

        [Fact]
        public void OldFormatFilesAreConvertedToMappings()
        {
            InputProfile p = ProfileImport.Parse("{\"KeyBindings\":{\"A / Cross\":\"Space\"}}", out _, out _);
            Assert.Null(p.KeyBindings);
            Assert.Equal("Space", p.Mappings["FACE_SOUTH"].Input);
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
                string written = System.IO.File.ReadAllText(path);
                Assert.Contains("\"Name\": \"Default\"", written);
                Assert.Contains("\"Space\"", written);          // the shipped Default config (PS4 to keyboard + mouse)

                System.IO.File.WriteAllText(path, "{\"Name\":\"EditedAgain\"}");
                Assert.False(DefaultProfile.PrepareStartup(dir, DefaultProfile.MappingVersion));   // already upgraded: left alone
                Assert.Equal("{\"Name\":\"EditedAgain\"}", System.IO.File.ReadAllText(path));
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        }
    }
}
