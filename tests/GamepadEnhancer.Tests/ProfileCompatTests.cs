using System.Text.Json;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class ProfileCompatTests
    {
        // A profile written by an older build: it still carries the removed drift / filtering / recoil / disengage fields.
        private const string OldProfile = @"{
  ""Name"": ""Old"",
  ""ControllerType"": ""Xbox 360"",
  ""BaseDriftPercent"": 32,
  ""RightStickJitter"": -10,
  ""RightStickJitterAlgorithm"": ""LowPass"",
  ""RightStickJitterButton"": ""Mouse Right"",
  ""RightStickJitterMode"": ""Hold"",
  ""RightStickJitterRateMs"": 2,
  ""BasePullDirection"": ""Down"",
  ""ScrollDisengageMs"": 500,
  ""WheelDisengageEnabled"": true,
  ""RecoilEnabled"": true,
  ""RecoilLoop"": false,
  ""RecoilTriggerKey"": ""Mouse Left"",
  ""RecoilPatternText"": ""0:0:100|0:18:80"",
  ""DisableKeys"": [""Tab"", ""E""],
  ""MouseLookEnabled"": true,
  ""MouseSensitivityX"": 12.5,
  ""KeyBindings"": { ""A / Cross"": ""Space"" },
  ""TurboEnabled"": { ""A / Cross"": true },
  ""TurboHz"": { ""A / Cross"": 20 }
}";

        [Fact]
        public void OldProfileStillLoadsAndKeepsTheSettingsThatRemain()
        {
            InputProfile p = JsonSerializer.Deserialize<InputProfile>(OldProfile);
            Assert.NotNull(p);
            Assert.Equal("Old", p.Name);
            Assert.Equal("Xbox 360", p.ControllerType);
            Assert.True(p.MouseLookEnabled);
            Assert.Equal(12.5, p.MouseSensitivityX);
            Assert.Equal("Space", p.KeyBindings["A / Cross"]);
            Assert.True(p.TurboEnabled["A / Cross"]);
            Assert.Equal(20, p.TurboHz["A / Cross"]);
        }

        [Fact]
        public void SavedProfileNoLongerContainsTheRemovedFields()
        {
            string json = JsonSerializer.Serialize(new InputProfile { Name = "New" });
            foreach (string gone in new[] { "Drift", "Jitter", "Recoil", "Disengage", "DisableKeys", "PullDirection" })
                Assert.DoesNotContain(gone, json);
        }

        [Fact]
        public void ShippedDefaultProfileLoadsWithoutTheRemovedFields()
        {
            InputProfile d = DefaultProfile.Create();
            Assert.Equal("Default", d.Name);
            Assert.Equal("PS4", d.ControllerType);
            Assert.NotEmpty(d.KeyBindings);
        }
    }
}
