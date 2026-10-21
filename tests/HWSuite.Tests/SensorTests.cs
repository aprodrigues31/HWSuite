using System.Globalization;
using HWSuite.Cli;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;
using HWSuite.Providers.Lhm;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

public class SensorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 21, 18, 30, 0, TimeSpan.FromHours(-3));

    private static SensorSnapshot Snapshot(params SensorReading[] readings) => new(At, readings);

    private static SensorReading Cpu(string name, SensorKind kind, double? value, double? min = null, double? max = null) =>
        new($"/amdcpu/0/{kind}/{name}", "AMD Ryzen 7 5800XT", "CPU", name, kind, value, min, max);

    [Theory]
    [InlineData(SensorKind.Temperature, 52.25, "52.3 °C")]
    [InlineData(SensorKind.Voltage, 1.3125, "1.313 V")]
    [InlineData(SensorKind.Power, 87.04, "87.0 W")]
    [InlineData(SensorKind.Clock, 4549.7, "4550 MHz")]
    [InlineData(SensorKind.Fan, 1234.4, "1234 RPM")]
    [InlineData(SensorKind.Load, 7.5, "7.5 %")]
    [InlineData(SensorKind.Throughput, 1572864.0, "1.5 MB/s")]
    [InlineData(SensorKind.Factor, 0.5, "0.5")]
    [InlineData(SensorKind.Factor, 1983.0, "1983")]
    [InlineData(SensorKind.Data, 21029.0, "20.5 TB")]
    [InlineData(SensorKind.SmallData, 16368.0, "16 GB")]
    [InlineData(SensorKind.Timing, 14.25, "14.25 ns")]
    public void Formats_values_with_units(SensorKind kind, double value, string expected) =>
        Assert.Equal(expected, SensorFormat.Format(value, kind, CultureInfo.InvariantCulture));

    [Fact]
    public void Missing_or_invalid_values_render_as_a_dash()
    {
        Assert.Equal("—", SensorFormat.Format(null, SensorKind.Temperature, CultureInfo.InvariantCulture));
        Assert.Equal("—", SensorFormat.Format(double.NaN, SensorKind.Temperature, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Text_table_groups_by_device_and_kind()
    {
        var snapshot = Snapshot(
            Cpu("Core (Tctl/Tdie)", SensorKind.Temperature, 61.5, 40, 78.25),
            Cpu("Package", SensorKind.Power, 88.2),
            new SensorReading("/lpc/nct6798d/fan/1", "Nuvoton NCT6798D", "Super I/O", "CPU Fan", SensorKind.Fan, 1180, 950, 1600)
            {
                Parent = "TUF GAMING B550M-PLUS",
            });

        using var text = new StringWriter(CultureInfo.InvariantCulture);
        SensorTextWriter.Write(snapshot, Strings.For("pt-BR"), text);
        var output = text.ToString();

        Assert.Contains("AMD Ryzen 7 5800XT  [CPU]", output, StringComparison.Ordinal);
        Assert.Contains("TUF GAMING B550M-PLUS › Nuvoton NCT6798D  [Super I/O]", output, StringComparison.Ordinal);
        Assert.Contains("Temperaturas", output, StringComparison.Ordinal);
        Assert.Contains("Ventoinhas", output, StringComparison.Ordinal);
        Assert.Contains("61,5 °C", output, StringComparison.Ordinal);
        Assert.Contains("78,3 °C", output, StringComparison.Ordinal);
        Assert.Contains("1180 RPM", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Sensors_without_any_reading_are_hidden()
    {
        var snapshot = Snapshot(Cpu("Core #1", SensorKind.Clock, null), Cpu("Core #1 (Effective)", SensorKind.Clock, 4450));

        using var text = new StringWriter(CultureInfo.InvariantCulture);
        SensorTextWriter.Write(snapshot, Strings.For("en"), text);
        var output = text.ToString();

        var coreLines = output.Split('\n').Where(l => l.Contains("Core #1", StringComparison.Ordinal)).ToList();
        Assert.Single(coreLines);
        Assert.Contains("Core #1 (Effective)", coreLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Zeroed_cpu_readings_are_flagged_as_blocked_low_level_access()
    {
        // What the user's Ryzen 7 5800XT returned from a non-elevated terminal.
        var blocked = Snapshot(
            Cpu("Core (Tctl/Tdie)", SensorKind.Temperature, 0),
            Cpu("Package", SensorKind.Power, 0),
            Cpu("Core #1", SensorKind.Clock, null),
            Cpu("CPU Total", SensorKind.Load, 3.9));
        var healthy = Snapshot(Cpu("Core (Tctl/Tdie)", SensorKind.Temperature, 47.5), Cpu("Package", SensorKind.Power, 0));
        var noCpuSensors = Snapshot(Cpu("CPU Total", SensorKind.Load, 3.9));

        Assert.True(SensorDiagnostics.CpuLowLevelReadsBlocked(blocked));
        Assert.False(SensorDiagnostics.CpuLowLevelReadsBlocked(healthy));
        Assert.False(SensorDiagnostics.CpuLowLevelReadsBlocked(noCpuSensors));
    }

    [Fact]
    public void Elevated_relaunch_keeps_the_sensor_options()
    {
        var watch = CliOptions.Parse(["sensors", "--watch", "--interval", "2"]);
        Assert.Equal("sensors --watch --interval 2", SensorsCommand.ElevatedArguments(watch));

        var log = CliOptions.Parse(["sensors", "--log", "temps.csv", "--duration", "60"]);
        var args = SensorsCommand.ElevatedArguments(log);
        Assert.StartsWith("sensors --log \"", args, StringComparison.Ordinal);
        Assert.Contains("temps.csv\" --interval 1 --duration 60", args, StringComparison.Ordinal);
        Assert.True(Path.IsPathRooted(args.Split('"')[1])); // absolute: the elevated copy may start in System32
    }

    private sealed class ScriptedSource(params double?[][] reads) : ISensorSource
    {
        private int _next;

        public string Name => "scripted";

        public IReadOnlyList<HWSuite.Core.Model.Diagnostic> Diagnostics => [];

        public SensorSnapshot Read() =>
            Snapshot([.. reads[_next++].Select((v, i) => Cpu($"S{i}", SensorKind.Temperature, v, min: 0, max: 0))]);

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Min_max_ignore_the_priming_read_and_library_values()
    {
        // First read: hardware not primed yet (zeros). Then 52, 50, 53.
        using var source = new MinMaxTrackingSource(new ScriptedSource([0, null], [52, 40], [50, null], [53, 41]));

        var first = source.Read();
        Assert.Null(first.Readings[0].Min);

        source.Read();
        source.Read();
        var last = source.Read();

        Assert.Equal((50.0, 53.0), (last.Readings[0].Min, last.Readings[0].Max));
        Assert.Equal((40.0, 41.0), (last.Readings[1].Min, last.Readings[1].Max));
    }

    [Fact]
    public void History_keeps_the_latest_values_aligned()
    {
        var history = new SensorHistory(capacity: 3);
        foreach (var v in new double?[] { 40, 41, null, 43 })
        {
            history.Add(Snapshot(Cpu("Tctl", SensorKind.Temperature, v)));
        }

        var values = history.Get("/amdcpu/0/Temperature/Tctl");
        Assert.Equal(3, values.Count);
        Assert.Equal(41.0, values[0]);
        Assert.True(double.IsNaN(values[1])); // gap kept so the timeline stays aligned
        Assert.Equal(43.0, values[2]);
        Assert.Empty(history.Get("unknown"));
    }

    [Fact]
    public void Empty_snapshot_says_so()
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        SensorTextWriter.Write(Snapshot(), Strings.For("en"), text);

        Assert.Contains("No sensors could be read.", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_log_keeps_columns_fixed_and_uses_invariant_numbers()
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        var logger = new SensorCsvLogger(text);

        logger.Append(Snapshot(Cpu("Core, Tctl", SensorKind.Temperature, 61.5), Cpu("Package", SensorKind.Power, 88.25)));
        logger.Append(Snapshot(Cpu("Package", SensorKind.Power, 90), Cpu("New", SensorKind.Load, 3)));

        var lines = text.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.Equal("timestamp,\"AMD Ryzen 7 5800XT / Core, Tctl [°C]\",AMD Ryzen 7 5800XT / Package [W]", lines[0]);
        Assert.Equal("2026-10-21T18:30:00.000-03:00,61.5,88.25", lines[1]);
        Assert.Equal("2026-10-21T18:30:00.000-03:00,,90", lines[2]); // missing sensor = empty cell, new sensor ignored
    }

    [Theory]
    [InlineData("Temperature", SensorKind.Temperature)]
    [InlineData("SmallData", SensorKind.SmallData)]
    [InlineData("Timing", SensorKind.Timing)] // DIMM timings, added in LibreHardwareMonitor 0.9.6
    [InlineData("SomethingNew", SensorKind.Other)] // unknown to HWSuite: kept, not dropped
    public void Maps_library_sensor_types(string libraryType, SensorKind expected) =>
        Assert.Equal(expected, LhmSensorSource.MapKind(libraryType));

    [Theory]
    [InlineData("GpuAmd", "GPU")]
    [InlineData("SuperIO", "Super I/O")]
    [InlineData("Storage", "Storage")]
    public void Names_hardware_kinds(string libraryType, string expected) =>
        Assert.Equal(expected, LhmSensorSource.HardwareKindName(libraryType));

    [Fact]
    public void Access_diagnostics_explain_missing_elevation_and_driver()
    {
        Assert.Empty(LhmSensorSource.AccessDiagnostics(new AccessStatus(IsElevated: true, PawnIoInstalled: true)));

        var notes = LhmSensorSource.AccessDiagnostics(new AccessStatus(IsElevated: false, PawnIoInstalled: false));
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Message.Contains("hwsuite driver install", StringComparison.Ordinal));
        Assert.All(notes, n => Assert.Equal(DiagnosticSeverity.Warning, n.Severity));
    }

    [Fact]
    public void Cli_parses_sensor_options()
    {
        var o = CliOptions.Parse(["sensors", "--log", "temps.csv", "-i", "0.5", "--duration", "60"]);

        Assert.Equal(CliCommand.Sensors, o.Command);
        Assert.Equal(("temps.csv", 0.5, (double?)60), (o.LogPath, o.IntervalSeconds, o.DurationSeconds));
        Assert.True(CliOptions.Parse(["sensors", "-w"]).Watch);

        var bad = Assert.Throws<CliUsageException>(() => CliOptions.Parse(["sensors", "--interval", "zero"]));
        Assert.Equal("cli.badNumber", bad.Key);

        // Sensor options are not accepted by the report command.
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["report", "--watch"]));
    }
}
