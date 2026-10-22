using HWSuite.Core.Pci;

namespace HWSuite.Core.Sensors;

/// <summary>
/// Turns AMD ADL performance-log values (ADL2_New_QueryPMLogData_Get: sensor number -> integer) into
/// readings. LibreHardwareMonitor already maps the common ones (clocks, edge/memory/hot spot
/// temperatures, fan, power); this adds what it leaves out: voltage regulator temperatures, the
/// RDNA 3 chiplet hot spots, the live PCIe link and the driver's throttling limits.
/// Raw mode lists every sensor the driver reports, numbered, to compare with other tools.
/// </summary>
public static class AdlTelemetry
{
    private sealed record Map(string Name, SensorKind Kind);

    // ADL_PMLOG_* numbers from the AMD Display Library SDK (adl_defines.h).
    private static readonly Dictionary<int, Map> Extras = new()
    {
        [24] = new("GPU VR SoC", SensorKind.Temperature),
        [25] = new("GPU VR Memory 0", SensorKind.Temperature),
        [26] = new("GPU VR Memory 1", SensorKind.Temperature),
        [42] = new("GPU Liquid 0", SensorKind.Temperature),
        [43] = new("GPU Liquid 1", SensorKind.Temperature),
        [50] = new("GPU Hot Spot (GCD)", SensorKind.Temperature),
        [51] = new("GPU Hot Spot (MCD)", SensorKind.Temperature),
        [52] = new("Limit: Edge Temperature", SensorKind.Load),
        [53] = new("Limit: Hot Spot Temperature", SensorKind.Load),
        [54] = new("Limit: Hot Spot Temperature (GCD)", SensorKind.Load),
        [55] = new("Limit: Hot Spot Temperature (MCD)", SensorKind.Load),
        [56] = new("Limit: Memory Temperature", SensorKind.Load),
        [57] = new("Limit: VR GFX Temperature", SensorKind.Load),
        [60] = new("Limit: VR SoC Temperature", SensorKind.Load),
        [64] = new("Limit: GFX TDC", SensorKind.Load),
        [65] = new("Limit: SoC TDC", SensorKind.Load),
        [66] = new("Limit: USR TDC", SensorKind.Load),
        [67] = new("Limit: PPT", SensorKind.Load),
    };

    private const int BusSpeed = 40;
    private const int BusLanes = 41;

    /// <summary>
    /// PCI vendor from an ADL adapter UDID ("PCI_VEN_1002&amp;DEV_747E&amp;..."). ADL's own VendorID field
    /// is wrong on Windows (1002 in decimal instead of 0x1002), as LibreHardwareMonitor also notes.
    /// </summary>
    public static int? VendorFromUdid(string? udid)
    {
        const string Marker = "PCI_VEN_";
        var at = udid?.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (at < 0)
        {
            return null;
        }

        var hex = udid!.AsSpan(at + Marker.Length);
        var end = hex.IndexOf('&');
        return int.TryParse(end < 0 ? hex : hex[..end], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var vendor)
            ? vendor
            : null;
    }

    /// <summary>ADL_PMLOG_* names by number, for raw listings.</summary>
    public static string SensorName(int number) => number switch
    {
        1 => "CLK_GFXCLK", 2 => "CLK_MEMCLK", 3 => "CLK_SOCCLK", 4 => "CLK_UVDCLK1", 5 => "CLK_UVDCLK2",
        6 => "CLK_VCECLK", 7 => "CLK_VCNCLK", 8 => "TEMPERATURE_EDGE", 9 => "TEMPERATURE_MEM",
        10 => "TEMPERATURE_VRVDDC", 11 => "TEMPERATURE_VRMVDD", 12 => "TEMPERATURE_LIQUID", 13 => "TEMPERATURE_PLX",
        14 => "FAN_RPM", 15 => "FAN_PERCENTAGE", 16 => "SOC_VOLTAGE", 17 => "SOC_POWER", 18 => "SOC_CURRENT",
        19 => "INFO_ACTIVITY_GFX", 20 => "INFO_ACTIVITY_MEM", 21 => "GFX_VOLTAGE", 22 => "MEM_VOLTAGE", 23 => "ASIC_POWER",
        24 => "TEMPERATURE_VRSOC", 25 => "TEMPERATURE_VRMVDD0", 26 => "TEMPERATURE_VRMVDD1", 27 => "TEMPERATURE_HOTSPOT",
        28 => "TEMPERATURE_GFX", 29 => "TEMPERATURE_SOC", 30 => "GFX_POWER", 31 => "GFX_CURRENT", 32 => "TEMPERATURE_CPU",
        33 => "CPU_POWER", 34 => "CLK_CPUCLK", 35 => "THROTTLER_STATUS", 36 => "CLK_VCN1CLK1", 37 => "CLK_VCN1CLK2",
        38 => "SMART_POWERSHIFT_CPU", 39 => "SMART_POWERSHIFT_DGPU", 40 => "BUS_SPEED", 41 => "BUS_LANES",
        42 => "TEMPERATURE_LIQUID0", 43 => "TEMPERATURE_LIQUID1", 44 => "CLK_FCLK", 45 => "THROTTLER_STATUS_CPU",
        46 => "SSPAIRED_ASICPOWER", 47 => "SSTOTAL_POWERLIMIT", 48 => "SSAPU_POWERLIMIT", 49 => "SSDGPU_POWERLIMIT",
        50 => "TEMPERATURE_HOTSPOT_GCD", 51 => "TEMPERATURE_HOTSPOT_MCD", 52 => "THROTTLER_TEMP_EDGE_PERCENTAGE",
        53 => "THROTTLER_TEMP_HOTSPOT_PERCENTAGE", 54 => "THROTTLER_TEMP_HOTSPOT_GCD_PERCENTAGE",
        55 => "THROTTLER_TEMP_HOTSPOT_MCD_PERCENTAGE", 56 => "THROTTLER_TEMP_MEM_PERCENTAGE",
        57 => "THROTTLER_TEMP_VR_GFX_PERCENTAGE", 58 => "THROTTLER_TEMP_VR_MEM0_PERCENTAGE",
        59 => "THROTTLER_TEMP_VR_MEM1_PERCENTAGE", 60 => "THROTTLER_TEMP_VR_SOC_PERCENTAGE",
        61 => "THROTTLER_TEMP_LIQUID0_PERCENTAGE", 62 => "THROTTLER_TEMP_LIQUID1_PERCENTAGE",
        63 => "THROTTLER_TEMP_PLX_PERCENTAGE", 64 => "THROTTLER_TDC_GFX_PERCENTAGE", 65 => "THROTTLER_TDC_SOC_PERCENTAGE",
        66 => "THROTTLER_TDC_USR_PERCENTAGE", 67 => "THROTTLER_PPT0_PERCENTAGE", 68 => "THROTTLER_PPT1_PERCENTAGE",
        69 => "THROTTLER_PPT2_PERCENTAGE", 70 => "THROTTLER_PPT3_PERCENTAGE", 71 => "THROTTLER_FIT_PERCENTAGE",
        72 => "THROTTLER_GFX_APCC_PLUS_PERCENTAGE", 73 => "BOARD_POWER",
        _ => "SENSOR_" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Readings for one GPU. <paramref name="values"/> holds only the sensors the driver marked as
    /// supported. <paramref name="hardware"/> should be the adapter name LibreHardwareMonitor uses,
    /// so these rows join the same GPU group.
    /// </summary>
    public static IReadOnlyList<SensorReading> Readings(string hardware, string idPrefix, IReadOnlyDictionary<int, int> values, bool raw)
    {
        ArgumentNullException.ThrowIfNull(values);
        var readings = new List<SensorReading>();

        if (raw)
        {
            foreach (var (number, value) in values.OrderBy(p => p.Key))
            {
                readings.Add(Reading(hardware, $"{idPrefix}/raw/{number}", $"ADL #{number} {SensorName(number)}", SensorKind.Other, value));
            }

            return readings;
        }

        foreach (var (number, map) in Extras)
        {
            if (values.TryGetValue(number, out var value) && (map.Kind != SensorKind.Temperature || value > 0))
            {
                readings.Add(Reading(hardware, $"{idPrefix}/{number}", map.Name, map.Kind, value));
            }
        }

        if (values.TryGetValue(BusSpeed, out var speed) && LinkSpeedGts(speed) is { } gts)
        {
            readings.Add(Reading(hardware, $"{idPrefix}/{BusSpeed}", "PCIe Link Speed (GT/s)", SensorKind.Factor, gts));
        }

        if (values.TryGetValue(BusLanes, out var lanes) && lanes is > 0 and <= 32)
        {
            readings.Add(Reading(hardware, $"{idPrefix}/{BusLanes}", "PCIe Link Width (lanes)", SensorKind.Factor, lanes));
        }

        return readings;
    }

    /// <summary>
    /// BUS_SPEED as GT/s. Drivers report either the link status encoding (1..6) or the rate itself
    /// (in GT/s or MT/s); all three map to the same scale here.
    /// </summary>
    public static double? LinkSpeedGts(int value) => value switch
    {
        >= 1 and <= 6 => PcieLink.GigaTransfers(value),
        2500 or 5000 or 8000 or 16000 or 32000 or 64000 => value / 1000.0,
        > 6 and <= 64 => value,
        _ => null,
    };

    private static SensorReading Reading(string hardware, string id, string name, SensorKind kind, double value) =>
        new(id, hardware, "GPU", name, kind, value, value, value);
}
