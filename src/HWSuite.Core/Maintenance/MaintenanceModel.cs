using HWSuite.Core.Benchmark;
using HWSuite.Core.Model;
using HWSuite.Core.Network;
using HWSuite.Core.Sensors;

namespace HWSuite.Core.Maintenance;

/// <summary>Outcome of one check, ordered from best to worst (the report's overall verdict is the worst one).</summary>
public enum Verdict
{
    /// <summary>Only information (nothing to judge, or no reading).</summary>
    Info,
    Ok,
    Attention,
    Problem,
}

/// <summary>Where a check belongs; the report groups checks by area in this order.</summary>
public enum CheckArea
{
    System,
    Processor,
    Memory,
    Storage,
    Graphics,
    Cooling,
    Power,
    Battery,
    Performance,
    Network,
}

/// <summary>
/// One line of the diagnosis: what was checked (<see cref="Item"/>), what was found, the verdict and, when
/// something should be done, a recommendation written for the client.
/// </summary>
public sealed record CheckResult(CheckArea Area, string Item, string Finding, Verdict Verdict, string? Advice = null);

/// <summary>Free space of one drive letter.</summary>
public sealed record VolumeSpace(string Name, long TotalBytes, long FreeBytes, bool IsSystem)
{
    public double FreePercent => TotalBytes > 0 ? FreeBytes * 100.0 / TotalBytes : 0;
}

/// <summary>Result of the processor load test run during the diagnosis.</summary>
/// <param name="Duration">How long it ran.</param>
/// <param name="Errors">Wrong results found by the stress test (any is a sign of instability).</param>
/// <param name="MaxCpuTemperature">Hottest processor reading during the test.</param>
/// <param name="MaxCpuPower">Highest package power.</param>
/// <param name="MinCpuClockMhz">Lowest average core clock under load (a big drop is throttling).</param>
public sealed record LoadTest(TimeSpan Duration, long Errors, double? MaxCpuTemperature, double? MaxCpuPower, double? MinCpuClockMhz)
{
    /// <summary>Processor temperature, power and clock every second of the test, for the report's charts.</summary>
    public Sensors.TimelineData? Timeline { get; init; }
}

/// <summary>Everything the checks look at. Only the inventory is required.</summary>
public sealed record MaintenanceInput(HardwareInventory Inventory, DateTimeOffset Now)
{
    /// <summary>A sensor reading taken at rest (temperatures, fans, voltages).</summary>
    public SensorSnapshot? Sensors { get; init; }

    public IReadOnlyList<VolumeSpace> Volumes { get; init; } = [];

    public LoadTest? Load { get; init; }

    /// <summary>Processor benchmark run during the diagnosis, compared with other runs of the same model.</summary>
    public BenchmarkComparison? Benchmark { get; init; }

    public BenchmarkResult? BenchmarkResult { get; init; }

    /// <summary>The latest saved Internet test, if recent.</summary>
    public NetHistoryEntry? Network { get; init; }

    public string? PumpSensorId { get; init; }

    public int PumpMinRpm { get; init; } = Settings.AppSettings.DefaultPumpMinRpm;
}

/// <summary>Who did the service (printed in the report header).</summary>
public sealed record CompanyProfile(
    string? Name,
    string? Document,
    string? Contact,
    string? Address,
    string? Technician,
    string? LogoDataUri = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Technician);
}

/// <summary>For whom and what was done (filled in for each report).</summary>
public sealed record ServiceOrder(
    string? Client,
    string? ClientContact,
    string? Equipment,
    string? OrderNumber,
    string? ServicesDone,
    string? Notes);

/// <summary>A finished diagnosis, ready to be written.</summary>
public sealed record MaintenanceReport(
    CompanyProfile Company,
    ServiceOrder Order,
    IReadOnlyList<CheckResult> Checks,
    HardwareInventory Inventory,
    DateTimeOffset At,
    string AppVersion,
    bool IncludeInventory = true)
{
    /// <summary>The load test, when one ran (its charts go in the report).</summary>
    public LoadTest? Load { get; init; }

    public Verdict Overall => Checks.Count == 0 ? Verdict.Info : Checks.Max(c => c.Verdict);

    public int Count(Verdict verdict) => Checks.Count(c => c.Verdict == verdict);
}

/// <summary>
/// End of updates of each Windows 11 version (Microsoft's Windows 11 release information, Home/Pro and
/// Enterprise/Education columns). Windows 10 ended on 2025-10-14.
/// </summary>
public static class WindowsLifecycle
{
    public static readonly DateOnly Windows10End = new(2025, 10, 14);

    private static readonly (int Build, string Version, DateOnly HomePro, DateOnly Enterprise)[] Windows11 =
    [
        (22000, "21H2", new(2023, 10, 10), new(2024, 10, 8)),
        (22621, "22H2", new(2024, 10, 8), new(2025, 10, 14)),
        (22631, "23H2", new(2025, 11, 11), new(2026, 11, 10)),
        (26100, "24H2", new(2026, 10, 13), new(2027, 10, 12)),
        (26200, "25H2", new(2027, 10, 12), new(2028, 10, 10)),
        (26300, "26H2", new(2028, 10, 10), new(2029, 10, 9)),
        (28000, "26H1", new(2028, 3, 14), new(2029, 3, 13)),
    ];

    /// <summary>The last day of updates for this build and edition, or null when unknown (Server, LTSC, Insider).</summary>
    public static DateOnly? EndOfUpdates(int build, string? edition)
    {
        if (edition is not null && (edition.Contains("Server", StringComparison.OrdinalIgnoreCase) || edition.Contains("LTSC", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (build < 22000)
        {
            return Windows10End;
        }

        var enterprise = edition is not null && (edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) || edition.Contains("Education", StringComparison.OrdinalIgnoreCase));
        foreach (var w in Windows11)
        {
            if (w.Build == build)
            {
                return enterprise ? w.Enterprise : w.HomePro;
            }
        }

        return null;
    }
}
