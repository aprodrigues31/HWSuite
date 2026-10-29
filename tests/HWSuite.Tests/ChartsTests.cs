using System.Globalization;
using HWSuite.Core.Reporting;
using HWSuite.Core.Sensors;

namespace HWSuite.Tests;

public sealed class ChartsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(-3));

    private static SensorSnapshot Snap(int second, params (string Name, SensorKind Kind, double? Value)[] readings) =>
        new(T0.AddSeconds(second), [.. readings.Select(r => new SensorReading("/x/" + r.Name, "CPU", "CPU", r.Name, r.Kind, r.Value, null, null))]);

    [Fact]
    public void Timeline_keeps_samples_aligned_and_drops_the_oldest()
    {
        var timeline = new SensorTimeline(capacity: 3);
        timeline.Add(Snap(0, ("Temp", SensorKind.Temperature, 40)));
        timeline.Add(Snap(1, ("Temp", SensorKind.Temperature, 41), ("Power", SensorKind.Power, 50)));
        timeline.Add(Snap(2, ("Temp", SensorKind.Temperature, null), ("Power", SensorKind.Power, 52)));
        timeline.Add(Snap(3, ("Temp", SensorKind.Temperature, 43), ("Power", SensorKind.Power, 53)));

        var data = timeline.Snapshot();
        Assert.Equal([T0.AddSeconds(1), T0.AddSeconds(2), T0.AddSeconds(3)], data.Times);
        var temp = data.Find("CPU / Temp")!;
        Assert.Equal(SensorKind.Temperature, temp.Sensor.Kind);
        Assert.Equal(41, temp.Values[0]);
        Assert.True(double.IsNaN(temp.Values[1]));
        Assert.Equal(43, temp.Values[2]);
        Assert.Equal([50, 52, 53], data.Find("CPU / Power")!.Values);
    }

    [Fact]
    public void Timeline_survives_compaction_with_a_late_sensor()
    {
        var timeline = new SensorTimeline(capacity: 8);
        for (var i = 0; i < 40; i++)
        {
            var readings = new List<(string, SensorKind, double?)> { ("Temp", SensorKind.Temperature, i) };
            if (i >= 35)
            {
                readings.Add(("Fan", SensorKind.Fan, 1000 + i));
            }

            timeline.Add(Snap(i, [.. readings]));
        }

        var data = timeline.Snapshot();
        Assert.Equal(8, data.Times.Count);
        Assert.Equal([32, 33, 34, 35, 36, 37, 38, 39], data.Find("CPU / Temp")!.Values);
        var fan = data.Find("CPU / Fan")!.Values;
        Assert.True(double.IsNaN(fan[0]) && double.IsNaN(fan[2]));
        Assert.Equal(1035, fan[3]);
        Assert.Equal(1039, fan[7]);
    }

    [Fact]
    public void Timeline_ignores_samples_older_than_the_last()
    {
        var timeline = new SensorTimeline();
        timeline.Add(Snap(10, ("Temp", SensorKind.Temperature, 50)));
        timeline.Add(Snap(2, ("Temp", SensorKind.Temperature, 40))); // a late seed after a live read
        timeline.Add(Snap(11, ("Temp", SensorKind.Temperature, 51)));
        Assert.Equal([T0.AddSeconds(10), T0.AddSeconds(11)], timeline.Snapshot().Times);
    }

    [Fact]
    public void Last_window_and_selection()
    {
        var timeline = new SensorTimeline();
        for (var i = 0; i < 120; i++)
        {
            timeline.Add(Snap(i, ("Temp", SensorKind.Temperature, i), ("Power", SensorKind.Power, i * 2)));
        }

        var last = timeline.Snapshot().Last(TimeSpan.FromSeconds(30)).Select(["CPU / Power", "missing"]);
        Assert.Equal(31, last.Times.Count);
        Assert.Single(last.Series);
        Assert.Equal(178, last.Series[0].Values[0]);
    }

    [Fact]
    public void Csv_log_round_trips_through_the_reader()
    {
        var csv = new StringWriter();
        var logger = new SensorCsvLogger(csv);
        logger.Append(Snap(0, ("Package", SensorKind.Temperature, 45.5), ("Core, total", SensorKind.Load, 12), ("Vcore", SensorKind.Voltage, null)));
        logger.Append(Snap(1, ("Package", SensorKind.Temperature, 47), ("Core, total", SensorKind.Load, 99.5), ("Vcore", SensorKind.Voltage, 1.25)));

        var data = SensorLogReader.Read(new StringReader(csv.ToString() + "garbage line\n"));
        Assert.Equal(2, data.Times.Count);
        Assert.Equal(T0.AddSeconds(1), data.Times[1]);
        var load = data.Find("CPU / Core, total")!;
        Assert.Equal(SensorKind.Load, load.Sensor.Kind);
        Assert.Equal("Core, total", load.Sensor.Name);
        Assert.Equal([12, 99.5], load.Values);
        var vcore = data.Find("CPU / Vcore")!;
        Assert.Equal(SensorKind.Voltage, vcore.Sensor.Kind);
        Assert.True(double.IsNaN(vcore.Values[0]));
        Assert.Equal(1.25, vcore.Values[1]);
        Assert.Throws<InvalidDataException>(() => SensorLogReader.Read(new StringReader("hello,world\n")));
    }

    [Theory]
    [InlineData("AMD Ryzen 7 5800XT", SensorCategory.Cpu)]
    [InlineData("AMD Radeon RX 7800 XT", SensorCategory.Gpu)]
    [InlineData("ASUS TUF GAMING B550M-PLUS › Nuvoton NCT6798D", SensorCategory.Motherboard)]
    [InlineData("WD Blue SN350 1TB", SensorCategory.Storage)]
    [InlineData("Generic Memory", SensorCategory.Memory)]
    [InlineData("Something else", SensorCategory.Other)]
    public void Log_devices_get_a_component(string hardware, SensorCategory expected) =>
        Assert.Equal(expected, TimelineSensor.GuessCategory(hardware));

    [Fact]
    public void Stats_ignore_missing_values()
    {
        var stats = SeriesStats.Of([double.NaN, 10, 20, double.NaN, 30])!;
        Assert.Equal((10, 20, 30, 30, 3), (stats.Min, stats.Average, stats.Max, stats.Last, stats.Count));
        Assert.Null(SeriesStats.Of([double.NaN]));
    }

    [Fact]
    public void Reducer_keeps_peaks()
    {
        var values = Enumerable.Range(0, 10_000).Select(i => i == 5_123 ? 500.0 : i == 7_777 ? -50 : 20).ToArray();
        var kept = ChartReducer.Indexes(values, 100);
        Assert.InRange(kept.Count, 100, 200);
        Assert.Contains(5_123, kept);
        Assert.Contains(7_777, kept);
        Assert.Equal(kept.OrderBy(i => i), kept);
        Assert.Equal(3, ChartReducer.Indexes([1, double.NaN, 2, 3], 100).Count);
    }

    [Theory]
    [InlineData(42, 79, 40, 80)]
    [InlineData(4498, 4500, 4200, 5000)] // flat clock stays flat
    [InlineData(0.9, 1.3, 0.9, 1.3)]
    public void Nice_ranges(double min, double max, double low, double high)
    {
        var (l, h) = ChartReducer.NiceRange(min, max);
        Assert.Equal(low, l, 6);
        Assert.Equal(high, h, 6);
    }

    [Fact]
    public void Svg_chart_has_lines_axis_and_legend()
    {
        var timeline = new SensorTimeline();
        for (var i = 0; i <= 60; i++)
        {
            timeline.Add(Snap(i, ("Temp <pkg>", SensorKind.Temperature, 40 + (i / 2.0))));
        }

        var svg = SvgChart.Render(timeline.Snapshot(), "°C", CultureInfo.GetCultureInfo("pt-BR"), elapsed: true);
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("<polyline", svg, StringComparison.Ordinal);
        Assert.Contains(">1:00<", svg, StringComparison.Ordinal);
        Assert.Contains("Temp &lt;pkg&gt;", svg, StringComparison.Ordinal);
        Assert.Contains("40 / 55 / 70", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_log_options_and_html()
    {
        var o = HWSuite.Cli.CliOptions.Parse(["log", "teste.csv", "--filter", "CPU", "-o", "g.html"]);
        Assert.Equal(HWSuite.Cli.CliCommand.Log, o.Command);
        Assert.Equal(("teste.csv", "CPU", "g.html"), (o.LogFile, o.Filter, o.OutputPath));
        Assert.Throws<HWSuite.Cli.CliUsageException>(() => HWSuite.Cli.CliOptions.Parse(["log", "a.csv", "b.csv"]));

        var timeline = new SensorTimeline();
        for (var i = 0; i < 30; i++)
        {
            timeline.Add(Snap(i, ("Package", SensorKind.Temperature, 50 + i), ("Fan", SensorKind.Fan, 900), ("Bus", SensorKind.Clock, 100)));
        }

        var html = HWSuite.Cli.LogCommand.Html(timeline.Snapshot().Select(["CPU / Package", "CPU / Fan"]), "teste.csv", everything: false, HWSuite.Core.Localization.Strings.For("pt-BR"));
        Assert.Contains("Log de sensores", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("<svg").Length - 1);
    }

    [Fact]
    public void Inventory_sections_know_their_component()
    {
        var inv = new HWSuite.Core.Model.HardwareInventory { MachineName = "PC" };
        inv.OperatingSystem = new() { Name = "Windows 11 Pro", Build = "26200.1" };
        inv.Bios = new() { Vendor = "AMI", Version = "1.0" };
        inv.Memory = new() { TotalInstalledBytes = 16L << 30, SlotCount = 2 };
        var sections = ReportModel.Build(inv, HWSuite.Core.Localization.Strings.For("en"));
        Assert.All(sections, x => Assert.StartsWith("section.", x.Key, StringComparison.Ordinal));
        Assert.Equal(InventoryCategory.System, sections.Single(x => x.Key == "section.os").Category);
        Assert.Equal(InventoryCategory.Motherboard, sections.Single(x => x.Key == "section.bios").Category);
        Assert.Equal(InventoryCategory.Memory, sections.Single(x => x.Key == "section.memory").Category);
    }
}
