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

public class BoardProfileTests
{
    private static SensorReading SuperIo(string type, int index, string name, SensorKind kind, double? value) =>
        new($"/lpc/nct6798d/0/{type}/{index}", "Nuvoton NCT6798D", "Super I/O", name, kind, value, value, value)
        {
            Parent = "ASUS TUF GAMING B550M-PLUS",
        };

    private sealed class FixedSource(params SensorReading[] readings) : ISensorSource
    {
        public string Name => "fixed";

        public IReadOnlyList<HWSuite.Core.Model.Diagnostic> Diagnostics => [];

        public SensorSnapshot Read() => new(DateTimeOffset.UnixEpoch, readings);

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData("ASUS TUF GAMING B550M-PLUS", true)]
    [InlineData("ASUS TUF GAMING B550M-PLUS (WI-FI)", true)]
    [InlineData("ASUS TUF GAMING B550-PLUS", false)]
    [InlineData("ASUS ROG STRIX B550-F GAMING (WI-FI)", false)]
    public void Tuf_B550M_profile_matches_both_variants(string board, bool expected)
    {
        Assert.Equal(expected, BoardProfiles.Find(board, "Nuvoton NCT6798D") is not null);
    }

    [Fact]
    public void Profile_requires_the_expected_chip()
    {
        Assert.Null(BoardProfiles.Find("ASUS TUF GAMING B550M-PLUS", "ITE IT8689E"));
        Assert.Null(BoardProfiles.Find(null, "Nuvoton NCT6798D"));
    }

    [Theory]
    [InlineData("/lpc/nct6798d/0/voltage/4", 4)]
    [InlineData("/lpc/nct6798d/0/fan/12", 12)]
    [InlineData("/amdcpu/0/temperature/x", null)]
    [InlineData("noslash", null)]
    public void Input_index_comes_from_the_end_of_the_id(string id, int? expected)
    {
        Assert.Equal(expected, BoardProfiles.InputIndex(id));
    }

    [Fact]
    public void Real_readings_from_the_users_board_get_names_and_dividers()
    {
        // Values from an elevated "hwsuite sensors" on a TUF GAMING B550M-PLUS with a Ryzen 7 5800XT.
        using var source = new BoardProfileSource(new FixedSource(
            SuperIo("voltage", 0, "Vcore", SensorKind.Voltage, 0.576),
            SuperIo("voltage", 1, "Voltage #2", SensorKind.Voltage, 1.008),
            SuperIo("voltage", 4, "Voltage #5", SensorKind.Voltage, 0.992),
            SuperIo("voltage", 7, "+3V Standby", SensorKind.Voltage, 3.392),
            SuperIo("temperature", 2, "Temperature #3", SensorKind.Temperature, 81),
            new SensorReading("/amdcpu/0/voltage/2", "AMD Ryzen 7 5800XT", "CPU", "Core (SVI2 TFN)", SensorKind.Voltage, 1.138, null, null)));

        var byId = source.Read().Readings.ToDictionary(r => r.Id);

        var vcore = byId["/lpc/nct6798d/0/voltage/0"];
        Assert.Equal("CPU Core (Vcore)", vcore.Name);
        Assert.Equal(1.152, vcore.Value!.Value, 3);
        Assert.Equal(1.152, vcore.Max!.Value, 3);
        Assert.True(Math.Abs(vcore.Value.Value - byId["/amdcpu/0/voltage/2"].Value!.Value) < 0.05); // matches the CPU's own report

        Assert.Equal("+5V", byId["/lpc/nct6798d/0/voltage/1"].Name);
        Assert.Equal(5.04, byId["/lpc/nct6798d/0/voltage/1"].Value!.Value, 2);
        Assert.Equal("+12V", byId["/lpc/nct6798d/0/voltage/4"].Name);
        Assert.Equal(11.904, byId["/lpc/nct6798d/0/voltage/4"].Value!.Value, 3);
        Assert.Equal("+3.3V Standby", byId["/lpc/nct6798d/0/voltage/7"].Name);
        Assert.Equal(3.392, byId["/lpc/nct6798d/0/voltage/7"].Value!.Value, 3); // already scaled by the library

        Assert.False(byId.ContainsKey("/lpc/nct6798d/0/temperature/2")); // floating SYSTIN (81 °C) hidden
        Assert.Equal(["CPU", "Super I/O"], byId.Values.Select(r => r.HardwareKind).Distinct().Order());
        Assert.Equal("ASUS TUF GAMING B550M-PLUS", Assert.Single(source.ProfilesInUse));
    }

    [Fact]
    public void Fans_and_vtt_match_hwinfo_on_the_users_board()
    {
        using var source = new BoardProfileSource(new FixedSource(
            SuperIo("voltage", 9, "CPU Termination", SensorKind.Voltage, 0.904),
            SuperIo("fan", 0, "Fan #1", SensorKind.Fan, 808),
            SuperIo("fan", 1, "Fan #2", SensorKind.Fan, 2224),
            SuperIo("fan", 2, "Fan #3", SensorKind.Fan, 1247),
            SuperIo("fan", 3, "Fan #4", SensorKind.Fan, 0),
            SuperIo("fan", 5, "Fan #6", SensorKind.Fan, 1500),
            SuperIo("fan", 6, "Fan #7", SensorKind.Fan, 1015),
            SuperIo("control", 3, "Fan #4", SensorKind.Control, 60),
            SuperIo("temperature", 0, "Temperature #1", SensorKind.Temperature, 41.5)));

        var byId = source.Read().Readings.ToDictionary(r => r.Id);

        Assert.Equal(1.808, byId["/lpc/nct6798d/0/voltage/9"].Value!.Value, 3); // HWiNFO: VTT 1.808 V
        Assert.Equal("CPU Fan (CPU_FAN)", byId["/lpc/nct6798d/0/fan/1"].Name);
        Assert.Equal("Chassis Fan 1 (CHA_FAN1)", byId["/lpc/nct6798d/0/fan/0"].Name);
        Assert.Equal("Chassis Fan 2 (CHA_FAN2)", byId["/lpc/nct6798d/0/fan/2"].Name);
        Assert.Equal("CPU Optional (CPU_OPT)", byId["/lpc/nct6798d/0/fan/6"].Name);
        Assert.False(byId.ContainsKey("/lpc/nct6798d/0/fan/3")); // empty header, 0 RPM
        Assert.Equal("Fan #6", byId["/lpc/nct6798d/0/fan/5"].Name); // spins: shown even without a name
        Assert.False(byId.ContainsKey("/lpc/nct6798d/0/control/3"));
        Assert.Equal("CPU", byId["/lpc/nct6798d/0/temperature/0"].Name);
    }

    [Fact]
    public void Hidden_inputs_are_dropped_and_other_boards_pass_through()
    {
        var profile = new BoardProfile("Test", new System.Text.RegularExpressions.Regex("TEST BOARD"), "NCT",
            [new BoardSensorRule(SensorKind.Fan, 4, Hide: true)]);
        var fan = new SensorReading("/lpc/nct6798d/0/fan/4", "Nuvoton NCT6798D", "Super I/O", "Fan #5", SensorKind.Fan, 0, null, null);
        Assert.Null(BoardProfiles.Apply(profile, fan));

        var other = SuperIo("voltage", 0, "Vcore", SensorKind.Voltage, 0.576) with { Parent = "Some Other Board" };
        using var source = new BoardProfileSource(new FixedSource(other));
        Assert.Equal(0.576, Assert.Single(source.Read().Readings).Value);
        Assert.Empty(source.ProfilesInUse);
    }
}
