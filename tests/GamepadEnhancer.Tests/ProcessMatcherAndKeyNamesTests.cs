using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class ProcessMatcherTests
    {
        [Fact]
        public void ParsesCommaSemicolonAndNewlinesAndStripsExe()
        {
            var list = ProcessMatcher.ParseList("Game.exe, other;\n  \"Third.EXE\" ,game");
            Assert.Equal(new[] { "game", "other", "third" }, list);
        }

        [Fact]
        public void MatchesCaseInsensitivelyWithOrWithoutExe()
        {
            var list = ProcessMatcher.ParseList("r5apex");
            Assert.True(ProcessMatcher.Matches("R5Apex.exe", list));
            Assert.True(ProcessMatcher.Matches("r5apex", list));
            Assert.False(ProcessMatcher.Matches("notepad", list));
        }

        [Fact]
        public void EmptyListOrNameNeverMatches()
        {
            Assert.False(ProcessMatcher.Matches("game", ProcessMatcher.ParseList("")));
            Assert.False(ProcessMatcher.Matches("", ProcessMatcher.ParseList("game")));
            Assert.False(ProcessMatcher.Matches(null, ProcessMatcher.ParseList("game")));
        }
    }

    public class KeyNamesTests
    {
        [Theory]
        [InlineData("Space", 0x20)]
        [InlineData("W", 0x57)]
        [InlineData("5", 0x35)]
        [InlineData("F1", 0x70)]
        [InlineData("F12", 0x7B)]
        [InlineData("Up", 0x26)]
        [InlineData("Home", 0x24)]
        public void NamesMapBackToVirtualKeys(string name, int vk)
        {
            Assert.Contains(vk, KeyNames.ToVirtualKeys(name));
            Assert.Equal(name, KeyNames.FromVirtualKey(vk));
        }

        [Fact]
        public void ModifiersIncludeSideSpecificCodes()
        {
            Assert.Equal(new[] { 0x10, 0xA0, 0xA1 }, KeyNames.ToVirtualKeys("Shift"));
        }

        [Fact]
        public void UnknownAndNoneGiveNothing()
        {
            Assert.Empty(KeyNames.ToVirtualKeys("None"));
            Assert.Empty(KeyNames.ToVirtualKeys("Banana"));
            Assert.Equal(new[] { 0x91 }, KeyNames.ToVirtualKeys("VK91"));
        }
    }
}

namespace GamepadEnhancer.Tests
{
    public class BlockHotkeysTests
    {
        [Fact]
        public void NamesRoundTripAndUnknownFallsBackToScrollLock()
        {
            foreach (string name in BlockHotkeys.Names)
                Assert.Equal(name, BlockHotkeys.FromVk(BlockHotkeys.ToVk(name)));
            Assert.Equal(0x91, BlockHotkeys.ToVk("nope"));
            Assert.Equal("Scroll Lock", BlockHotkeys.FromVk(0xFF));
        }
    }
}
