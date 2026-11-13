namespace HWSuite.Core.Maintenance;

/// <summary>Where Windows keeps a startup entry.</summary>
public enum StartupSource
{
    /// <summary>HKCU\...\Run: this user only.</summary>
    UserRun,

    /// <summary>HKLM\...\Run: every user (64-bit programs).</summary>
    MachineRun,

    /// <summary>HKLM\...\WOW6432Node\...\Run: every user (32-bit programs).</summary>
    MachineRun32,

    /// <summary>The user's Startup folder.</summary>
    UserFolder,

    /// <summary>The Startup folder of all users.</summary>
    CommonFolder,

    /// <summary>A scheduled task that runs at sign-in or at boot (outside Windows' own \Microsoft folder).</summary>
    Task,
}

/// <summary>How much a program slows down Windows startup, with Task Manager's own thresholds.</summary>
public enum StartupImpact
{
    NotMeasured,
    NotRunning,
    Low,
    Medium,
    High,
}

/// <summary>
/// What a startup program used in its first minutes: processor time, disk reads and memory, and how long after
/// boot it started.
/// </summary>
public sealed record ImpactSample(string Executable, double CpuMs, long DiskReadBytes, double MemoryMb, double StartedAfterBootS)
{
    /// <summary>
    /// Task Manager's rule: high = more than 1 s of processor or 3 MB of disk reads; medium = 300 ms to 1 s or
    /// 300 KB to 3 MB; low = less.
    /// </summary>
    public StartupImpact Impact => CpuMs > 1000 || DiskReadBytes > 3 * 1024 * 1024 ? StartupImpact.High
        : CpuMs >= 300 || DiskReadBytes >= 300 * 1024 ? StartupImpact.Medium
        : StartupImpact.Low;
}

/// <summary>The last boot as Windows measured it (Diagnostics-Performance event 100) and the programs it blamed.</summary>
/// <param name="TotalMs">From power on to a usable desktop.</param>
/// <param name="MainPathMs">Windows itself (up to the desktop).</param>
/// <param name="PostBootMs">After the desktop appeared, while startup programs loaded.</param>
/// <param name="SlowApps">Programs Windows reported as slowing startup (event 101): file name → extra milliseconds.</param>
public sealed record BootReport(DateTime When, double TotalMs, double MainPathMs, double PostBootMs, IReadOnlyDictionary<string, double> SlowApps);

/// <summary>
/// One program that starts with Windows, as Task Manager shows it on its Startup apps tab.
/// </summary>
/// <param name="Name">Registry value name or shortcut file name (also the key of its on/off switch).</param>
/// <param name="Command">The command line (or the shortcut's file path).</param>
/// <param name="Executable">The program file, when it could be worked out from the command.</param>
/// <param name="Publisher">Company name from the program's version information.</param>
/// <param name="Description">File description from the program's version information.</param>
/// <param name="Enabled">Starts at sign-in (off = disabled the way Task Manager does it, nothing deleted).</param>
/// <param name="FileMissing">The program file no longer exists (left over by an uninstalled app).</param>
public sealed record StartupEntry(
    StartupSource Source,
    string Name,
    string Command,
    string? Executable,
    string? Publisher,
    string? Description,
    bool Enabled,
    bool FileMissing)
{
    /// <summary>Measured use of its program at the last boot (or now), when it was running.</summary>
    public ImpactSample? Sample { get; init; }

    /// <summary>Extra milliseconds Windows says this program added to the last boot (event 101), if any.</summary>
    public double? SlowedBootMs { get; init; }

    public StartupImpact Impact => !Enabled ? StartupImpact.NotRunning : Sample?.Impact ?? StartupImpact.NotMeasured;

    /// <summary>For every user: changing it needs administrator rights.</summary>
    public bool NeedsAdmin => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder or StartupSource.Task;

    /// <summary>Name shown to people: the program's description when it has one, else the entry name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description!;

    /// <summary>Stable id for the command line: "user-run:OneDrive".</summary>
    public string Id => $"{Source}:{Name}";
}

/// <summary>
/// The rules Windows uses for startup entries, kept apart from the registry so they can be tested:
/// the "StartupApproved" switch (what Task Manager writes) and finding the program in a command line.
/// </summary>
public static class StartupRules
{
    /// <summary>
    /// A StartupApproved value: 12 bytes, the first one even (02, 06) = enabled, odd (03, 07…) = disabled, the
    /// last 8 = when it was disabled (FILETIME). No value at all = enabled.
    /// </summary>
    public static bool IsEnabled(byte[]? approved) => approved is not { Length: > 0 } || (approved[0] & 1) == 0;

    /// <summary>The value to write to switch an entry on or off (Task Manager's own format).</summary>
    public static byte[] Approved(bool enabled, DateTime utcNow)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled)
        {
            BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(value, 4);
        }

        return value;
    }

    /// <summary>
    /// The program in a command line: the quoted part, or the text up to ".exe" (paths with spaces and no
    /// quotes are common in Run keys), or the first word. Environment variables are expanded by the caller.
    /// </summary>
    public static string? Executable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }

        foreach (var ext in new[] { ".exe", ".cmd", ".bat", ".lnk", ".com" })
        {
            var i = text.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (i > 0 && (i + ext.Length == text.Length || text[i + ext.Length] is ' ' or '"' or ','))
            {
                return text[..(i + ext.Length)];
            }
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? text[..space] : text;
    }

    /// <summary>Sorted for the list: enabled first, heaviest first, then by name.</summary>
    public static IReadOnlyList<StartupEntry> Sort(IEnumerable<StartupEntry> entries) =>
        [.. entries.OrderByDescending(e => e.Enabled).ThenByDescending(e => e.Impact).ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>A scheduled task's definition (its XML): does it run at sign-in or boot, is it on, what does it run.</summary>
    public static (bool AtStartup, bool Enabled, string? Command, string? Arguments, string? Author, string? Description) ParseTask(string xml)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns = doc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
            var triggers = doc.Root?.Element(ns + "Triggers");
            bool On(System.Xml.Linq.XElement? e) => e?.Element(ns + "Enabled")?.Value is not { } v || !v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
            var atStartup = triggers?.Elements().Any(t => (t.Name.LocalName is "LogonTrigger" or "BootTrigger") && On(t)) ?? false;
            var exec = doc.Root?.Element(ns + "Actions")?.Element(ns + "Exec");
            var info = doc.Root?.Element(ns + "RegistrationInfo");
            return (atStartup, On(doc.Root?.Element(ns + "Settings")), exec?.Element(ns + "Command")?.Value.Trim(), exec?.Element(ns + "Arguments")?.Value.Trim(),
                info?.Element(ns + "Author")?.Value.Trim(), info?.Element(ns + "Description")?.Value.Trim());
        }
        catch (System.Xml.XmlException)
        {
            return (false, false, null, null, null, null);
        }
    }

    /// <summary>Named data of a Windows event in XML (EventData/Data Name="...").</summary>
    public static IReadOnlyDictionary<string, string> EventData(System.Xml.Linq.XElement evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return evt.Descendants().Where(e => e.Name.LocalName == "Data" && e.Attribute("Name") is not null)
            .GroupBy(e => e.Attribute("Name")!.Value)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The last boot from the Diagnostics-Performance log (events as "wevtutil qe /f:xml" prints them, newest
    /// first): event 100 for the times, the 101 events right after it for the programs that slowed it down.
    /// </summary>
    public static BootReport? ParseBoot(string eventsXml)
    {
        try
        {
            var root = System.Xml.Linq.XElement.Parse("<Events>" + eventsXml + "</Events>");
            var events = root.Elements().Where(e => e.Name.LocalName == "Event").ToList();
            static int Id(System.Xml.Linq.XElement e) => int.TryParse(e.Descendants().FirstOrDefault(x => x.Name.LocalName == "EventID")?.Value, out var id) ? id : 0;
            static DateTime When(System.Xml.Linq.XElement e) =>
                DateTime.TryParse(e.Descendants().FirstOrDefault(x => x.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
            var boot = events.FirstOrDefault(e => Id(e) == 100);
            if (boot is null)
            {
                return null;
            }

            double Ms(IReadOnlyDictionary<string, string> d, string key) => d.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
            var data = EventData(boot);
            var bootAt = When(boot);
            var slow = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events.Where(e => Id(e) == 101 && Math.Abs((When(e) - bootAt).TotalMinutes) < 30))
            {
                var d = EventData(e);
                if (d.TryGetValue("FileName", out var file) && !string.IsNullOrWhiteSpace(file))
                {
                    var name = file.Trim()[(file.Trim().LastIndexOfAny(['\\', '/']) + 1)..];
                    slow[name] = Math.Max(slow.GetValueOrDefault(name), Ms(d, "DegradationTime"));
                }
            }

            return new BootReport(bootAt, Ms(data, "BootTime"), Ms(data, "MainPathBootTime"), Ms(data, "BootPostBootTime"), slow);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Saved boot measurement: first line the boot time (UTC ticks), then one tab-separated sample per line.</summary>
    public static string FormatSamples(DateTime bootUtc, IEnumerable<ImpactSample> samples)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var lines = new List<string> { bootUtc.Ticks.ToString(c) };
        lines.AddRange(samples.Select(x => string.Join('\t', x.Executable, x.CpuMs.ToString("0", c), x.DiskReadBytes.ToString(c), x.MemoryMb.ToString("0.0", c), x.StartedAfterBootS.ToString("0", c))));
        return string.Join('\n', lines);
    }

    public static (DateTime BootUtc, IReadOnlyList<ImpactSample> Samples)? ParseSamples(string text)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !long.TryParse(lines[0], System.Globalization.NumberStyles.None, c, out var ticks))
        {
            return null;
        }

        var samples = new List<ImpactSample>();
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split('\t');
            if (f.Length == 5 && double.TryParse(f[1], System.Globalization.NumberStyles.Float, c, out var cpu) && long.TryParse(f[2], System.Globalization.NumberStyles.None, c, out var disk)
                && double.TryParse(f[3], System.Globalization.NumberStyles.Float, c, out var mem) && double.TryParse(f[4], System.Globalization.NumberStyles.Float, c, out var after))
            {
                samples.Add(new ImpactSample(f[0], cpu, disk, mem, after));
            }
        }

        return (new DateTime(ticks, DateTimeKind.Utc), samples);
    }
}
