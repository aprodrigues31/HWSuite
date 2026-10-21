namespace HWSuite.Core.Sensors;

/// <summary>Picks the few values worth showing in a tray tooltip: CPU and GPU temperature.</summary>
public static class SensorSummary
{
    // Sensor names LibreHardwareMonitor uses for the temperature that represents the whole chip.
    private static readonly string[] CpuPreferred = ["Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tctl)", "Tdie", "Core Max"];
    private static readonly string[] GpuPreferred = ["GPU Core", "GPU Temperature", "Core"];

    public static double? CpuTemperature(SensorSnapshot snapshot) => Pick(snapshot, "CPU", CpuPreferred);

    public static double? GpuTemperature(SensorSnapshot snapshot) => Pick(snapshot, "GPU", GpuPreferred);

    /// <summary>"HWSuite · CPU 52 °C · GPU 48 °C", trimmed to what a tray tooltip can hold.</summary>
    public static string Tooltip(SensorSnapshot? snapshot, Localization.Strings text, int maxLength = 127)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string> { "HWSuite" };
        if (snapshot is not null)
        {
            if (CpuTemperature(snapshot) is { } cpu)
            {
                parts.Add($"CPU {SensorFormat.Format(cpu, SensorKind.Temperature, text.Culture)}");
            }

            if (GpuTemperature(snapshot) is { } gpu)
            {
                parts.Add($"GPU {SensorFormat.Format(gpu, SensorKind.Temperature, text.Culture)}");
            }
        }

        var tip = string.Join(" · ", parts);
        return tip.Length <= maxLength ? tip : tip[..maxLength];
    }

    private static double? Pick(SensorSnapshot snapshot, string hardwareKind, string[] preferred)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var temps = snapshot.Readings
            .Where(r => r.Kind == SensorKind.Temperature
                && string.Equals(r.HardwareKind, hardwareKind, StringComparison.OrdinalIgnoreCase)
                && r.Value is { } v && double.IsFinite(v) && v > 0)
            .ToList();
        if (temps.Count == 0)
        {
            return null;
        }

        foreach (var name in preferred)
        {
            var match = temps.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Value;
            }
        }

        return temps.Max(r => r.Value);
    }
}
