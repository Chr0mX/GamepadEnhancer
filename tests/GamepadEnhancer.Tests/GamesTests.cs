using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-test-" + Guid.NewGuid().ToString("N"));
        public TempDir() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    public class GameCatalogTests
    {
        private static readonly Dictionary<string, string> ApexExpected = new()
        {
            ["A / Cross"] = "Jump",
            ["B / Circle"] = "Crouch (toggle)",
            ["X / Square"] = "Interact / Pickup / Reload  ·  hold: Interact / Pickup",
            ["Y / Triangle"] = "Cycle Weapon / Holster  ·  hold: Holster",
            ["LB / L1"] = "Tactical Ability",
            ["RB / R1"] = "Ping  ·  hold: Ping Wheel",
            ["LT / L2"] = "Aim Down Sight",
            ["RT / R2"] = "Attack",
            ["LS / L3"] = "Sprint / Toggle Zoom",
            ["RS / R3"] = "Melee",
            ["DPad Up"] = "Use Health/Shield Kit  ·  hold: Health Wheel",
            ["DPad Right"] = "Toggle Fire Mode  ·  hold: Equip Survival Item",
            ["DPad Down"] = "Equip Grenade  ·  hold: Grenade Wheel",
            ["DPad Left"] = "Extra Character Action  ·  hold: Emote Wheel",
            ["Back / Share"] = "Map (toggle)",
            ["Start / Options"] = "Inventory (toggle)"
        };

        [Fact]
        public void ApexNativeLayoutMatchesTheTranscribedPreset()
        {
            GameDefinition apex = GameCatalog.Get("apex");
            Assert.Equal("Apex Legends", apex.Name);
            Assert.True(apex.Verified);
            Assert.False(string.IsNullOrWhiteSpace(apex.Source));
            foreach (var pair in ApexExpected) Assert.Equal(pair.Value, apex.NativeLabel(pair.Key));
            Assert.Equal(ApexExpected.Count, apex.Native.Count);
            // actions that are not on the preset have no label (nothing is invented)
            Assert.Null(apex.NativeLabel(ControllerEngine.ActionGuidePs));
            Assert.Null(apex.NativeLabel(ControllerEngine.ActionTouchpad));
            Assert.Null(apex.NativeLabel(ControllerEngine.ActionLeftStickUp));
        }

        [Fact]
        public void WarzoneIsListedButHonestlyUnverifiedAndShowsNothing()
        {
            GameDefinition wz = GameCatalog.Get("warzone");
            Assert.Equal("Call of Duty: Warzone", wz.Name);
            Assert.False(wz.Verified);
            Assert.Empty(wz.Native);
            Assert.Contains("NOT VERIFIED", wz.Source);
            foreach (string a in ControllerEngine.BindingActions) Assert.Null(wz.NativeLabel(a));
        }

        [Fact]
        public void GamesDoNotShareDefaultsOrLabels()
        {
            GameDefinition apex = GameCatalog.Get("apex"), wz = GameCatalog.Get("warzone");
            Assert.NotEqual(apex.Id, wz.Id);
            Assert.NotNull(GameProfiles.BuiltIn(apex));      // Apex ships PC-input defaults
            Assert.Null(GameProfiles.BuiltIn(wz));           // Warzone ships none until verified, it does not borrow Apex's
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-game")]
        [InlineData("../apex")]
        public void UnknownGamesFallBackToTheGenericEntry(string id) =>
            Assert.Equal(GameCatalog.GenericId, GameCatalog.Get(id).Id);

        [Fact]
        public void CatalogListsEveryGameOnceAndGenericLast()
        {
            var ids = GameCatalog.All.Select(g => g.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(GameCatalog.GenericId, ids[^1]);
            Assert.All(ids, id => Assert.True(GameCatalog.IsValidId(id)));
        }

        [Fact]
        public void ParseRejectsBadFilesAndNeverShowsUnverifiedLabels()
        {
            Assert.Null(GameCatalog.Parse("not json", out string e1)); Assert.NotNull(e1);
            Assert.Null(GameCatalog.Parse("{\"Id\":\"../x\",\"Name\":\"X\"}", out string e2)); Assert.NotNull(e2);
            GameDefinition g = GameCatalog.Parse("{\"Id\":\"newgame\",\"Name\":\"New\",\"Verified\":false,\"Native\":{\"A / Cross\":\"Jump\"}}", out _);
            Assert.NotNull(g);
            Assert.Null(g.NativeLabel("A / Cross"));
            GameDefinition v = GameCatalog.Parse("{\"Id\":\"newgame\",\"Name\":\"New\",\"Verified\":true,\"Native\":{\"A / Cross\":\"Jump\"}}", out _);
            Assert.Equal("Jump", v.NativeLabel("a / cross"));   // case-insensitive
        }
    }

    public class GameProfileStoreTests
    {
        private static InputProfile Custom(string cross)
        {
            InputProfile p = DefaultProfile.CreateApex();
            p.Mappings["A / Cross"].Input = cross;
            return p;
        }

        private static InputProfile GlobalMarker() { InputProfile p = DefaultProfile.Create(); p.Name = "GLOBAL"; return p; }

        [Fact]
        public void ResolveFollowsTheThreeLevelPriority()
        {
            using var dir = new TempDir();
            var store = new GameProfileStore(dir.Path);
            GameDefinition apex = GameCatalog.Get("apex"), wz = GameCatalog.Get("warzone");

            // 2) no user file: built-in default for Apex
            InputProfile p = GameProfiles.Resolve(apex, store, GlobalMarker, out ProfileSource src);
            Assert.Equal(ProfileSource.BuiltIn, src);
            Assert.Equal("Space", ProfileMappings.Resolve(p)["A / Cross"].Input);

            // 3) a game with no built-in profile falls back to the global one
            p = GameProfiles.Resolve(wz, store, GlobalMarker, out src);
            Assert.Equal(ProfileSource.Global, src);
            Assert.Equal("GLOBAL", p.Name);

            // 1) a saved customization wins
            Assert.True(store.SaveUser("apex", Custom("Z")));
            p = GameProfiles.Resolve(apex, store, GlobalMarker, out src);
            Assert.Equal(ProfileSource.User, src);
            Assert.Equal("Z", ProfileMappings.Resolve(p)["A / Cross"].Input);
        }

        [Fact]
        public void CustomizationsAreIndependentPerGame()
        {
            using var dir = new TempDir();
            var store = new GameProfileStore(dir.Path);
            Assert.True(store.SaveUser("apex", Custom("Z")));
            Assert.True(store.HasUser("apex"));
            Assert.False(store.HasUser("warzone"));

            InputProfile wzCustom = DefaultProfile.Create();
            wzCustom.Mappings["B / Circle"].Input = "X";
            Assert.True(store.SaveUser("warzone", wzCustom));

            Assert.Equal("Z", ProfileMappings.Resolve(store.LoadUser("apex"))["A / Cross"].Input);
            Assert.Equal("None", ProfileMappings.Resolve(store.LoadUser("warzone"))["A / Cross"].Input);
            Assert.Equal("X", ProfileMappings.Resolve(store.LoadUser("warzone"))["B / Circle"].Input);

            Assert.True(store.DeleteUser("apex"));                  // restoring Apex defaults...
            Assert.True(store.HasUser("warzone"));                  // ...leaves Warzone's customization alone
            Assert.Equal("Space", ProfileMappings.Resolve(GameProfiles.Resolve(GameCatalog.Get("apex"), store, GlobalMarker, out _))["A / Cross"].Input);
        }

        [Fact]
        public void CustomizationSurvivesARestart()
        {
            using var dir = new TempDir();
            InputProfile custom = Custom("Z");
            custom.Paddles = PaddleMappings.Build(new[]
            {
                new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.Key, Input = "R" },
                PaddleSetting.Off, PaddleSetting.Off,
                new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.PadButton, Input = ControllerEngine.ActionACross }
            });
            Assert.True(new GameProfileStore(dir.Path).SaveUser("apex", custom));

            var reopened = new GameProfileStore(dir.Path);   // a new store on the same folder = a new app run
            InputProfile loaded = reopened.LoadUser("apex");
            Assert.NotNull(loaded);
            Assert.Equal("Z", ProfileMappings.Resolve(loaded)["A / Cross"].Input);
            PaddleSetting[] pads = PaddleMappings.Resolve(loaded);
            Assert.True(pads[0].Enabled); Assert.Equal(PaddleTargetKind.Key, pads[0].Kind); Assert.Equal("R", pads[0].Input);
            Assert.False(pads[1].Enabled);
            Assert.Equal(PaddleTargetKind.PadButton, pads[3].Kind);
        }

        [Fact]
        public void BadIdsAndBrokenFilesAreHandledSafely()
        {
            using var dir = new TempDir();
            var store = new GameProfileStore(dir.Path);
            Assert.Throws<ArgumentException>(() => store.PathFor("../evil"));
            Assert.False(store.HasUser("../evil"));
            Assert.Null(store.LoadUser("apex"));
            File.WriteAllText(store.PathFor("apex"), "{ not json");
            Assert.Null(store.LoadUser("apex"));                       // corrupt: ignored, built-in default is used instead
            Assert.Equal(ProfileSource.BuiltIn, ResolveSource(store));
            Assert.False(File.Exists(store.PathFor("apex") + ".tmp"));
        }

        private static ProfileSource ResolveSource(GameProfileStore s) { GameProfiles.Resolve(GameCatalog.Get("apex"), s, null, out ProfileSource src); return src; }

        [Fact]
        public void EquivalenceDetectsRealChangesOnly()
        {
            InputProfile a = DefaultProfile.CreateApex(), b = DefaultProfile.CreateApex();
            Assert.True(GameProfiles.Equivalent(a, b));
            b.Mappings["A / Cross"].Input = "Z";
            Assert.False(GameProfiles.Equivalent(a, b));
            b = DefaultProfile.CreateApex();
            b.Paddles = PaddleMappings.Build(new[] { new PaddleSetting { Enabled = true, Kind = PaddleTargetKind.Key, Input = "R" }, PaddleSetting.Off, PaddleSetting.Off, PaddleSetting.Off });
            Assert.False(GameProfiles.Equivalent(a, b));
            b = DefaultProfile.CreateApex();
            b.Paddles = PaddleMappings.Build(Array.Empty<PaddleSetting>());   // all off == no paddle block
            Assert.True(GameProfiles.Equivalent(a, b));
        }

        [Fact]
        public void RestoringDefaultsReturnsTheBuiltInProfile()
        {
            InputProfile defaults = GameProfiles.Defaults(GameCatalog.Get("apex"), GlobalMarker);
            Assert.True(GameProfiles.Equivalent(defaults, DefaultProfile.CreateApex()));
            InputProfile generic = GameProfiles.Defaults(GameCatalog.Get("warzone"), GlobalMarker);
            Assert.Equal("GLOBAL", generic.Name);
        }
    }

    public class MigrationTests
    {
        [Fact]
        public void SettingsFromBeforeGamesStartOnApexAndKeepEverythingElse()
        {
            AppSettings s = JsonSerializer.Deserialize<AppSettings>("{\"InputCaptureMethod\":\"RawInput\",\"NetworkApiPort\":6000}");
            Assert.Equal("apex", s.ActiveGame);
            Assert.Equal(new[] { 0, 1, 2, 3 }, s.PaddleSourceIndex);
            Assert.Equal("RawInput", s.InputCaptureMethod);
            Assert.Equal(6000, s.NetworkApiPort);
        }

        [Fact]
        public void OldProfilesLoadWithPaddlesOffAndBecomeNewFormatOnSave()
        {
            const string old = "{\"Name\":\"Old\",\"KeyBindings\":{\"A / Cross\":\"Space\"},\"TurboEnabled\":{\"A / Cross\":true},\"TurboHz\":{\"A / Cross\":20}}";
            InputProfile p = ProfileImport.Parse(old, out string err, out _);
            Assert.Null(err);
            Assert.Null(p.Paddles);                                      // untouched: no paddle block invented
            Assert.All(PaddleMappings.Resolve(p), s => Assert.False(s.Enabled));
            Assert.Equal("Space", p.Mappings["A / Cross"].Input);
            Assert.True(p.Mappings["A / Cross"].Turbo);

            using var dir = new TempDir();
            var store = new GameProfileStore(dir.Path);
            Assert.True(store.SaveUser("generic", p));
            Assert.Contains("\"Mappings\"", File.ReadAllText(store.PathFor("generic")));
            Assert.Equal("Space", ProfileMappings.Resolve(store.LoadUser("generic"))["A / Cross"].Input);
        }

        [Fact]
        public void ImportClearsInvalidPaddleTargetsAndDuplicates()
        {
            const string json = "{\"Paddles\":{" +
                "\"LeftUpper\":{\"Enabled\":true,\"Kind\":\"Key\",\"Input\":\"R\"}," +
                "\"LeftLower\":{\"Enabled\":true,\"Kind\":\"Key\",\"Input\":\"r\"}," +
                "\"RightUpper\":{\"Enabled\":true,\"Kind\":\"MouseButton\",\"Input\":\"Banana\"}," +
                "\"RightLower\":{\"Enabled\":true,\"Kind\":\"PadButton\",\"Input\":\"LS / L3\"}}}";
            InputProfile p = ProfileImport.Parse(json, out _, out List<string> warnings);
            PaddleSetting[] s = PaddleMappings.Resolve(p);
            Assert.True(s[0].IsActive);
            Assert.False(s[1].IsActive);                                   // duplicate of LeftUpper
            Assert.False(s[2].HasTarget);                                  // unknown mouse button
            Assert.Equal(PaddleTargetKind.PadButton, s[3].Kind);
            Assert.True(warnings.Count >= 2);
        }
    }
}
