using System.Text.RegularExpressions;

namespace HWSuite.Core.Sensors;

/// <summary>The processor block of the dashboard. Values are null when the sensor is not available.</summary>
public sealed record CpuReadout(
    string? Name,
    double? Temperature,
    string? TemperatureId,
    double? Load,
    double? ClockMhz,
    double? PowerW,
    double? CoreVolts,
    IReadOnlyList<double> ThreadLoads);

/// <summary>The graphics card block.</summary>
public sealed record GpuReadout(
    string? Name,
    double? Temperature,
    string? TemperatureId,
    double? HotSpot,
    double? Load,
    double? ClockMhz,
    double? PowerW,
    double? MemoryUsedMb,
    double? MemoryTotalMb);

/// <summary>System memory in use.</summary>
public sealed record MemoryReadout(double? UsedGb, double? TotalGb, double? Load);

/// <summary>One drive: temperature and how full it is.</summary>
public sealed record DriveReadout(string Name, double? Temperature, double? UsedPercent);

/// <summary>A fan or pump header that is spinning.</summary>
public sealed record FanReadout(string Id, string Name, double Rpm);

/// <summary>Everything the dashboard shows, picked out of one snapshot.</summary>
public sealed record DashboardReadout(
    CpuReadout Cpu,
    GpuReadout Gpu,
    MemoryReadout Memory,
    IReadOnlyList<DriveReadout> Drives,
    IReadOnlyList<FanReadout> Fans,
    IReadOnlyList<(PowerRail Rail, double Volts)> Rails);

/// <summary>
/// Picks the headline values out of a snapshot by the sensor names hardware libraries use (LibreHardwareMonitor,
/// AMD ADL): "CPU Total", "CPU Core #1 Thread #2", "Package", "GPU Core", "GPU Hot Spot", "Memory Used", "Used Space".
/// A sensor that is missing just leaves its value empty.
/// </summary>
public static partial class Dashboard
{
    public static DashboardReadout From(SensorSnapshot snapshot, string? pumpSensorId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var all = snapshot.Readings.Where(r => r.Value is { } v && double.IsFinite(v)).ToList();
        return new DashboardReadout(
            Cpu(all, snapshot),
            Gpu(all, snapshot),
            Memory(all),
            Drives(all),
            Fans(all, pumpSensorId),
            PowerRails.Read(snapshot));
    }

    private static CpuReadout Cpu(List<SensorReading> all, SensorSnapshot snapshot)
    {
        var cpu = all.Where(r => r.HardwareKind == "CPU").ToList();
        var temp = Preferred(cpu, SensorKind.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tctl)", "Tdie", "Core Max");
        var load = Preferred(cpu, SensorKind.Load, "CPU Total", "Total");
        var power = Preferred(cpu, SensorKind.Power, "Package", "CPU Package");
        var volts = cpu.FirstOrDefault(r => r.Kind == SensorKind.Voltage && r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(r => r.Kind == SensorKind.Voltage && r.HardwareKind == "Super I/O" && r.Name.Equals("Vcore", StringComparison.OrdinalIgnoreCase));
        var clocks = cpu.Where(r => r.Kind == SensorKind.Clock && CoreClock().IsMatch(r.Name) && r.Value > 0).Select(r => r.Value!.Value).ToList();

        // Thread loads in core/thread order ("CPU Core #2 Thread #1"); older libraries name only cores.
        var threads = cpu.Where(r => r.Kind == SensorKind.Load && r.Name.StartsWith("CPU Core #", StringComparison.Ordinal))
            .Select(r => (Key: Order(r.Name), r.Value!.Value))
            .OrderBy(x => x.Key.Core).ThenBy(x => x.Key.Thread)
            .Select(x => Math.Clamp(x.Value, 0, 100))
            .ToList();

        var name = cpu.Select(r => r.Hardware).FirstOrDefault();
        var tempValue = temp?.Value ?? SensorSummary.CpuTemperature(snapshot);
        return new CpuReadout(name, tempValue, temp?.Id, load?.Value, clocks.Count > 0 ? clocks.Average() : null, power?.Value, volts?.Value, threads);
    }

    private static GpuReadout Gpu(List<SensorReading> all, SensorSnapshot snapshot)
    {
        // The card with a temperature reading (an integrated GPU often has none) and real names first.
        var gpus = all.Where(r => r.HardwareKind == "GPU" && !r.Hardware.Contains('(', StringComparison.Ordinal)).GroupBy(r => r.Hardware).ToList();
        var gpu = gpus.OrderByDescending(g => g.Any(r => r.Kind == SensorKind.Temperature)).ThenByDescending(g => g.Count()).FirstOrDefault()?.ToList() ?? [];
        var temp = Preferred(gpu, SensorKind.Temperature, "GPU Core", "GPU Temperature", "Core", "GPU Edge", "Edge");
        var hotSpot = gpu.FirstOrDefault(r => r.Kind == SensorKind.Temperature && (r.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase)));
        var load = Preferred(gpu, SensorKind.Load, "GPU Core", "D3D 3D", "GPU");
        var clock = Preferred(gpu, SensorKind.Clock, "GPU Core", "Core", "GPU Shader");
        var power = Preferred(gpu, SensorKind.Power, "GPU Package", "GPU Power", "GPU Core", "Total Board Power");
        var used = Preferred(gpu, SensorKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        var total = Preferred(gpu, SensorKind.SmallData, "GPU Memory Total");
        return new GpuReadout(gpu.Select(r => r.Hardware).FirstOrDefault(), temp?.Value ?? SensorSummary.GpuTemperature(snapshot), temp?.Id,
            hotSpot?.Value, load?.Value, clock?.Value, power?.Value, used?.Value, total?.Value);
    }

    private static MemoryReadout Memory(List<SensorReading> all)
    {
        // "Memory" (physical) rather than "Virtual Memory".
        var mem = all.Where(r => r.HardwareKind == "Memory" && !r.Name.StartsWith("Virtual", StringComparison.OrdinalIgnoreCase)).ToList();
        var used = Preferred(mem, SensorKind.Data, "Memory Used", "Used Memory");
        var available = Preferred(mem, SensorKind.Data, "Memory Available", "Available Memory");
        var load = Preferred(mem, SensorKind.Load, "Memory");
        double? total = used?.Value + available?.Value;
        var loadValue = load?.Value ?? (total > 0 ? used!.Value * 100 / total : null);
        return new MemoryReadout(used?.Value, total, loadValue);
    }

    private static List<DriveReadout> Drives(List<SensorReading> all) =>
        [.. all.Where(r => r.HardwareKind == "Storage")
            .GroupBy(r => r.Hardware)
            .Select(g => new DriveReadout(
                g.Key,
                (Preferred([.. g], SensorKind.Temperature, "Composite Temperature", "Temperature") ?? g.FirstOrDefault(r => r.Kind == SensorKind.Temperature))?.Value,
                g.FirstOrDefault(r => r.Kind == SensorKind.Load && r.Name.Contains("Used Space", StringComparison.OrdinalIgnoreCase))?.Value))
            .Where(d => d.Temperature is not null || d.UsedPercent is not null)];

    /// <summary>Spinning fans, the pump first, then the others in the order the board lists them.</summary>
    private static List<FanReadout> Fans(List<SensorReading> all, string? pumpSensorId) =>
        [.. all.Where(r => r.Kind == SensorKind.Fan && r.Value > 0)
            .Select((r, i) => (Reading: r, Order: r.Id == pumpSensorId ? -1 : i))
            .OrderBy(x => x.Order)
            .Select(x => new FanReadout(x.Reading.Id, x.Reading.Name, x.Reading.Value!.Value))];

    private static SensorReading? Preferred(List<SensorReading> readings, SensorKind kind, params string[] names)
    {
        foreach (var name in names)
        {
            var match = readings.FirstOrDefault(r => r.Kind == kind && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static (int Core, int Thread) Order(string name)
    {
        var m = CoreThread().Match(name);
        return m.Success
            ? (int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Success ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0)
            : (int.MaxValue, 0);
    }

    [GeneratedRegex(@"^Core #\d+$")]
    private static partial Regex CoreClock();

    [GeneratedRegex(@"Core #(\d+)(?: Thread #(\d+))?")]
    private static partial Regex CoreThread();
}

/// <summary>
/// Where a value sits on a sensible scale for its kind (0..1), for the bars of the sensor list: temperatures
/// on 0–100 °C, loads and levels on 0–100 %, voltages around their nominal value, anything else between the
/// lowest and highest value seen.
/// </summary>
public static class SensorScale
{
    public static double Fraction(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.Value is not { } v || !double.IsFinite(v))
        {
            return 0;
        }

        double f = reading.Kind switch
        {
            SensorKind.Temperature => v / 100,
            SensorKind.Load or SensorKind.Level or SensorKind.Control or SensorKind.Humidity => v / 100,
            SensorKind.Voltage when PowerRails.RailOf(reading) is { } rail => (v - (rail.Nominal * 0.9)) / (rail.Nominal * 0.2),
            _ when reading.Max is { } max && max > 0 => v / max,
            _ => 0,
        };
        return Math.Clamp(f, 0, 1);
    }
}

/// <summary>The component a sensor belongs to, as the Sensors page groups and filters them.</summary>
public enum SensorCategory
{
    Motherboard,
    Cpu,
    Memory,
    Storage,
    Gpu,
    Network,
    Battery,
    Other,
}

/// <summary>Maps the hardware kind libraries report ("CPU", "Super I/O", "Storage"...) to a <see cref="SensorCategory"/>.</summary>
public static class SensorCategories
{
    public static SensorCategory Of(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return Of(reading.HardwareKind);
    }

    public static SensorCategory Of(string hardwareKind) => hardwareKind.ToUpperInvariant() switch
    {
        "MOTHERBOARD" or "SUPER I/O" or "SUPERIO" or "EC" or "EMBEDDEDCONTROLLER" => SensorCategory.Motherboard,
        "CPU" => SensorCategory.Cpu,
        "MEMORY" => SensorCategory.Memory,
        "STORAGE" => SensorCategory.Storage,
        "GPU" or "GPUAMD" or "GPUNVIDIA" or "GPUINTEL" => SensorCategory.Gpu,
        "NETWORK" => SensorCategory.Network,
        "BATTERY" => SensorCategory.Battery,
        _ => SensorCategory.Other,
    };

    /// <summary>Saved choice ("Cpu,Gpu") back to categories; unknown names are ignored, nothing means everything.</summary>
    public static IReadOnlySet<SensorCategory> Parse(string? saved) =>
        (saved ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<SensorCategory>(x, out var c) ? (SensorCategory?)c : null)
            .OfType<SensorCategory>()
            .ToHashSet();

    public static string? Format(IEnumerable<SensorCategory> categories)
    {
        var list = categories.Distinct().Order().ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }
}
