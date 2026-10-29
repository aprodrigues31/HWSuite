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
            { "name": "hwsuite-setup-0.14.0.exe", "browser_download_url": "https://github.com/aprodrigues31/HWSuite/releases/download/v0.14.0/hwsuite-setup-0.14.0.exe", "digest": "sha256:F82CDA8FCAC127BA23D917EA10B8A5FD60E63C8C5B8ACB766687E443776D89BA" }
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
        Assert.Equal("f82cda8fcac127ba23d917ea10b8a5fd60e63c8c5b8acb766687e443776d89ba", release.InstallerSha256); // the real 0.14.0 setup
    }

    private sealed class FakeServer(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private static ReleaseInfo WithInstaller(string? sha) =>
        new("0.15.0", new Uri("https://github.com/aprodrigues31/HWSuite/releases/tag/v0.15.0"),
            new Uri("https://github.com/aprodrigues31/HWSuite/releases/download/v0.15.0/hwsuite-setup-0.15.0.exe"), null) { InstallerSha256 = sha };

    [Fact]
    public async Task The_installer_is_kept_only_when_its_checksum_matches()
    {
        var body = System.Text.Encoding.ASCII.GetBytes("fake installer bytes");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        var folder = Path.Combine(Path.GetTempPath(), $"hwsuite-update-{Guid.NewGuid():N}");
        try
        {
            using var http = new HttpClient(new FakeServer(body));
            var path = await UpdateChecker.DownloadInstallerAsync(http, WithInstaller(sha), folder);
            Assert.Equal(Path.Combine(folder, "hwsuite-setup-0.15.0.exe"), path);
            Assert.True(body.AsSpan().SequenceEqual(File.ReadAllBytes(path!)));

            using var tampered = new HttpClient(new FakeServer([.. body, 0x00]));
            File.Delete(path!);
            Assert.Null(await UpdateChecker.DownloadInstallerAsync(tampered, WithInstaller(sha), folder));
            Assert.False(File.Exists(path)); // a file that does not match is deleted, never run

            Assert.Null(await UpdateChecker.DownloadInstallerAsync(http, WithInstaller(null), folder)); // no published digest: refused
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
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
