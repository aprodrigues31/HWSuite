using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HWSuite.Core.Updates;

/// <summary>The latest published release: version and where to get it.</summary>
public sealed record ReleaseInfo(string Version, Uri Page, Uri? Installer, Uri? Portable);

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
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                    if (name.StartsWith("hwsuite-setup-", StringComparison.OrdinalIgnoreCase))
                    {
                        installer = url;
                    }
                    else if (name.StartsWith("hwsuite-gui-", StringComparison.OrdinalIgnoreCase))
                    {
                        portable = url;
                    }
                }
            }

            return new ReleaseInfo(tag.GetString()!.TrimStart('v', 'V'), new Uri(page.GetString()!), installer, portable);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException or ArgumentNullException)
        {
            return null;
        }
    }

    /// <summary>"0.14.0" is newer than "0.13.0"; anything unparsable is not.</summary>
    public static bool IsNewer(string latest, string current) =>
        System.Version.TryParse(Core(latest), out var l) && System.Version.TryParse(Core(current), out var c) && l > c;

    /// <summary>"0.13.0-beta+build.4" -> "0.13.0".</summary>
    private static string Core(string version) => version.Split('-', '+')[0].Trim();
}
