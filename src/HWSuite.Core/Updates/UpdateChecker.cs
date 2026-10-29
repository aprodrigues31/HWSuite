using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HWSuite.Core.Updates;

/// <summary>The latest published release: version and where to get it.</summary>
public sealed record ReleaseInfo(string Version, Uri Page, Uri? Installer, Uri? Portable)
{
    /// <summary>SHA-256 of the installer as GitHub computed it on upload (hex, lower case), when published.</summary>
    public string? InstallerSha256 { get; init; }
}

/// <summary>
/// Asks GitHub for the latest HWSuite release (one anonymous request to the public API, nothing about the
/// machine is sent) and tells whether it is newer than the running version. Any failure means "no news".
/// </summary>
public static class UpdateChecker
{
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/aprodrigues31/HWSuite/releases/latest");

    public static async Task<ReleaseInfo?> LatestAsync(HttpClient http, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HWSuite", BuildInfo.Current.Version));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>Reads tag_name, html_url and the setup / desktop-app assets of a GitHub release.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || !root.TryGetProperty("html_url", out var page)
                || root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
                || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            {
                return null;
            }

            Uri? installer = null, portable = null;
            string? installerSha = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                    if (name.StartsWith("hwsuite-setup-", StringComparison.OrdinalIgnoreCase))
                    {
                        installer = url;
                        if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        {
                            installerSha = d[7..].ToLowerInvariant();
                        }
                    }
                    else if (name.StartsWith("hwsuite-gui-", StringComparison.OrdinalIgnoreCase))
                    {
                        portable = url;
                    }
                }
            }

            return new ReleaseInfo(tag.GetString()!.TrimStart('v', 'V'), new Uri(page.GetString()!), installer, portable) { InstallerSha256 = installerSha };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException or ArgumentNullException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to <paramref name="folder"/> and checks it against the SHA-256 GitHub published.
    /// Returns the file path, or null on any failure (the partial file is deleted). Without a published
    /// digest it refuses: an unverified executable is never run.
    /// </summary>
    public static async Task<string?> DownloadInstallerAsync(HttpClient http, ReleaseInfo release, string folder, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(release);
        if (release.Installer is null || release.InstallerSha256 is null)
        {
            return null;
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(release.Installer.LocalPath));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Installer);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HWSuite", BuildInfo.Current.Version));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var file = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (total is > 0)
                    {
                        progress?.Report((double)done / total.Value);
                    }
                }
            }

            if (!HashMatches(sha.GetHashAndReset(), release.InstallerSha256))
            {
                File.Delete(path);
                return null;
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // left in the temporary folder
            }

            return null;
        }
    }

    internal static bool HashMatches(byte[] hash, string expectedHex) =>
        string.Equals(Convert.ToHexString(hash), expectedHex, StringComparison.OrdinalIgnoreCase);

    /// <summary>"0.14.0" is newer than "0.13.0"; anything unparsable is not.</summary>
    public static bool IsNewer(string latest, string current) =>
        System.Version.TryParse(Core(latest), out var l) && System.Version.TryParse(Core(current), out var c) && l > c;

    /// <summary>"0.13.0-beta+build.4" -> "0.13.0".</summary>
    private static string Core(string version) => version.Split('-', '+')[0].Trim();
}
