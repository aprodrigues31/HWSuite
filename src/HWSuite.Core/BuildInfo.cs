using System.Reflection;
using System.Text.RegularExpressions;

namespace HWSuite.Core;

/// <summary>
/// Version of the running tool, parsed from the informational version stamped at build time:
/// "0.2.0+build.12.a3b8c29f..." → version 0.2.0, build 12, commit a3b8c29.
/// </summary>
public sealed partial record BuildInfo(string Version, int? Build, string? Commit)
{
    public static BuildInfo Current { get; } =
        Parse((Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"0.2.0 (build 12, a3b8c29)"; local builds without CI metadata show just the version.</summary>
    public string Display
    {
        get
        {
            var details = new List<string>(2);
            if (Build is > 0)
            {
                details.Add($"build {Build}");
            }

            if (Commit is not null)
            {
                details.Add(Commit);
            }

            return details.Count == 0 ? Version : $"{Version} ({string.Join(", ", details)})";
        }
    }

    public static BuildInfo Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new BuildInfo("0.0.0", null, null);
        }

        var match = Pattern().Match(informationalVersion);
        if (!match.Success)
        {
            return new BuildInfo(informationalVersion.Split('+')[0], null, null);
        }

        int? build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var commit = match.Groups["commit"].Success ? match.Groups["commit"].Value[..Math.Min(7, match.Groups["commit"].Value.Length)] : null;
        return new BuildInfo(match.Groups["version"].Value, build, commit);
    }

    [GeneratedRegex(@"^(?<version>[^+]+)(?:\+(?:build\.(?<build>\d+))?\.?(?<commit>[0-9a-fA-F]{7,40})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
