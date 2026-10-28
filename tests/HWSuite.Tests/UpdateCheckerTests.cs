using HWSuite.Core.Updates;

namespace HWSuite.Tests;

public class UpdateCheckerTests
{
    private const string Release = """
        {
          "tag_name": "v0.14.0",
          "html_url": "https://github.com/aprodrigues31/HWSuite/releases/tag/v0.14.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "hwsuite-0.14.0-win-x64.zip", "browser_download_url": "https://github.com/aprodrigues31/HWSuite/releases/download/v0.14.0/hwsuite-0.14.0-win-x64.zip" },
            { "name": "hwsuite-gui-0.14.0-win-x64.zip", "browser_download_url": "https://github.com/aprodrigues31/HWSuite/releases/download/v0.14.0/hwsuite-gui-0.14.0-win-x64.zip" },
            { "name": "hwsuite-setup-0.14.0.exe", "browser_download_url": "https://github.com/aprodrigues31/HWSuite/releases/download/v0.14.0/hwsuite-setup-0.14.0.exe" }
          ]
        }
        """;

    [Fact]
    public void Release_json_is_read()
    {
        var release = UpdateChecker.Parse(Release)!;
        Assert.Equal("0.14.0", release.Version);
        Assert.EndsWith("hwsuite-setup-0.14.0.exe", release.Installer!.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("hwsuite-gui-0.14.0-win-x64.zip", release.Portable!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Drafts_prereleases_and_junk_are_ignored()
    {
        Assert.Null(UpdateChecker.Parse(Release.Replace("\"prerelease\": false", "\"prerelease\": true", StringComparison.Ordinal)));
        Assert.Null(UpdateChecker.Parse("{\"message\": \"Not Found\"}"));
        Assert.Null(UpdateChecker.Parse("not json"));
    }

    [Theory]
    [InlineData("0.14.0", "0.13.0", true)]
    [InlineData("0.13.0", "0.13.0", false)]
    [InlineData("0.13.0", "0.14.0", false)]
    [InlineData("1.0.0", "0.99.0", true)]
    [InlineData("0.10.0", "0.9.0", true)] // numeric, not text, comparison
    [InlineData("garbage", "0.13.0", false)]
    public void Newer_versions(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(latest, current));
}
