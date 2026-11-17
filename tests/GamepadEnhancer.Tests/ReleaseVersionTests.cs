using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GamepadEnhancer.Tests
{
    /// <summary>The version shown in the app (csproj &lt;Version&gt;) must follow the newest release notes, so a release never reports an old version.</summary>
    public class ReleaseVersionTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GamepadEnhancer.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        [Fact]
        public void AppVersionMatchesNewestReleaseNotes()
        {
            string root = Root();
            string csproj = File.ReadAllText(Path.Combine(root, "src", "GamepadEnhancer", "GamepadEnhancer.csproj"));
            Match m = Regex.Match(csproj, @"<Version>(\d+)\.(\d+)(?:\.\d+)?");
            Assert.True(m.Success, "csproj has no numeric <Version>");
            var app = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));

            var notes = Directory.GetFiles(Path.Combine(root, "docs", "releases"), "v*.md")
                .Select(f => Regex.Match(Path.GetFileName(f), @"^v(\d+)\.(\d+)\.md$"))
                .Where(x => x.Success)
                .Select(x => (int.Parse(x.Groups[1].Value), int.Parse(x.Groups[2].Value)))
                .OrderBy(v => v).Last();

            Assert.Equal(notes, app);
        }
    }
}
