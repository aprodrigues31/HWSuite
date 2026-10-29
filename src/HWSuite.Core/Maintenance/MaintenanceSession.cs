using HWSuite.Core.Benchmark;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;

namespace HWSuite.Core.Maintenance;

/// <summary>
/// Gathers what the diagnosis needs besides the inventory, the same way for the app and the command line:
/// free space of the fixed drives, the company profile and logo, and the processor load test (the stress test
/// runs while the sensors are read every second, keeping the hottest temperature, highest power and lowest clock).
/// </summary>
public static class MaintenanceSession
{
    /// <summary>Documents\HWSuite\Reports, where reports are saved.</summary>
    public static string ReportsFolder => Path.Combine(BenchmarkFiles.Folder, "Reports");

    private static readonly string[] LogoNames = ["logo.png", "logo.jpg", "logo.jpeg", "logo.svg"];

    /// <summary>Fixed drives that are ready, the Windows drive marked.</summary>
    public static IReadOnlyList<VolumeSpace> Volumes()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        var list = new List<VolumeSpace>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady && drive.TotalSize > 0)
                {
                    list.Add(new VolumeSpace(drive.Name.TrimEnd('\\', '/'), drive.TotalSize, drive.AvailableFreeSpace,
                        string.Equals(drive.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a drive that disappears or denies access is just left out
            }
        }

        return list;
    }

    /// <summary>The logo file the user dropped in the HWSuite folder (logo.png, .jpg or .svg), or null.</summary>
    public static string? LogoPath(string? folder = null) =>
        LogoNames.Select(n => Path.Combine(folder ?? BenchmarkFiles.Folder, n)).FirstOrDefault(File.Exists);

    /// <summary>The logo as a data URI, so the report is one self-contained file (null above 2 MB or without a logo).</summary>
    public static string? LogoDataUri(string? folder = null) => LogoPath(folder) is { } path ? LogoFromFile(path) : null;

    /// <summary>An image file (PNG, JPEG or SVG, up to 2 MB) as a data URI; null when it is missing or not usable.</summary>
    public static string? LogoFromFile(string path)
    {
        try
        {
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => null,
            };
            return mime is null || !File.Exists(path) || new FileInfo(path).Length > 2 << 20
                ? null
                : $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The company for the report. The logo is the one chosen in the settings ("" = none); when never chosen, a
    /// logo.png/.jpg/.svg in the HWSuite folder is used if there is one.
    /// </summary>
    public static CompanyProfile Company(AppSettings settings, string? logoFolder = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var logo = settings.LogoFile switch
        {
            "" => null,
            { } file => LogoFromFile(file),
            null => LogoDataUri(logoFolder),
        };
        return new CompanyProfile(settings.CompanyName, settings.CompanyDocument, settings.CompanyContact, settings.CompanyAddress, settings.TechnicianName, logo);
    }

    /// <summary>
    /// Runs the processor stress test for <paramref name="duration"/>, reading the sensors through
    /// <paramref name="read"/> every second. The first seconds are left out of the minimum clock (boost settles).
    /// </summary>
    public static LoadTest RunLoad(TimeSpan duration, Func<SensorSnapshot?> read, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stop.CancelAfter(duration);
        StressStatus? final = null;
        var stress = Task.Run(() => final = CpuStressTest.Run(null, cancel: stop.Token), CancellationToken.None);

        double? maxTemp = null, maxPower = null, minClock = null;
        var times = new List<DateTimeOffset>();
        List<double> temps = [], powers = [], clocks = [];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!stress.IsCompleted)
        {
            stress.Wait(TimeSpan.FromSeconds(1), CancellationToken.None);
            if (read() is { } snapshot)
            {
                var cpu = Dashboard.From(snapshot).Cpu;
                times.Add(snapshot.Timestamp);
                temps.Add(cpu.Temperature ?? double.NaN);
                powers.Add(cpu.PowerW ?? double.NaN);
                clocks.Add(cpu.ClockMhz ?? double.NaN);
                maxTemp = Max(maxTemp, cpu.Temperature);
                maxPower = Max(maxPower, cpu.PowerW);
                if (clock.Elapsed > TimeSpan.FromSeconds(3) && cpu.ClockMhz is { } mhz)
                {
                    minClock = minClock is { } m ? Math.Min(m, mhz) : mhz;
                }
            }

            progress?.Report(Math.Min(1, clock.Elapsed / duration));
        }

        cancel.ThrowIfCancellationRequested();
        return new LoadTest(clock.Elapsed, final?.Errors ?? 0, maxTemp, maxPower, minClock)
        {
            Timeline = new TimelineData(times,
            [
                new(new TimelineSensor("cpu.temperature", "CPU", "Temperature", SensorKind.Temperature), temps),
                new(new TimelineSensor("cpu.power", "CPU", "Power", SensorKind.Power), powers),
                new(new TimelineSensor("cpu.clock", "CPU", "Clock", SensorKind.Clock), clocks),
            ]),
        };
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
