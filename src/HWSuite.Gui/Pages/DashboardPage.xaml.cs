using System.Globalization;
using System.Runtime.InteropServices;
using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Gui.Hud;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HWSuite.Gui.Pages;

/// <summary>
/// The start page: the machine at a glance, live. Processor as the central ring (temperature, with load as
/// the inner arc), per-thread load, graphics card, memory, five minutes of CPU/GPU temperature, fans and pump,
/// the power supply rails, and the drives. Values that have no sensor show "—".
/// </summary>
public sealed partial class DashboardPage : Page
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public DashboardPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = s["dash.tag"].ToUpper(c);
        UptimeLabel.Text = s["dash.uptime"].ToUpper(c);
        IntervalLabel.Text = s["dash.interval"].ToUpper(c);
        CpuTag.Text = ("// " + s["dash.cpu"]).ToUpper(c);
        GpuTag.Text = ("// " + s["dash.gpu"]).ToUpper(c);
        MemTag.Text = ("// " + s["dash.memory"]).ToUpper(c);
        ClockLabel.Text = s["dash.clock"].ToUpper(c);
        PowerLabel.Text = s["dash.power"].ToUpper(c);
        VoltLabel.Text = "VCORE";
        ThreadsLabel.Text = s["dash.threads"].ToUpper(c);
        ChartTag.Text = ("// " + s.Format("dash.temps", s.Format("dash.minutes", App.Monitor.History.Capacity * App.Monitor.Interval.TotalMinutes))).ToUpper(c);
        CoolingTag.Text = ("// " + s["dash.cooling"]).ToUpper(c);
        StorageTag.Text = ("// " + s["dash.storage"]).ToUpper(c);
        MachineText.Text = $"{Environment.MachineName} · {RuntimeInformation.OSDescription}";
        BoardName.Text = Environment.MachineName.ToUpper(c);
        StatusText.Text = s["dash.waiting"].ToUpper(c);

        foreach (var value in new[] { ClockValue, PowerValue, VoltValue })
        {
            value.Text.Style = HudUi.Style("HudValueStyle");
            value.Text.Foreground = HudUi.Accent;
        }

        foreach (var value in new[] { GpuClock, MemUsed })
        {
            value.Text.FontFamily = HudUi.Font("HudMonoStrongFont");
            value.Text.FontSize = 26;
            value.Text.Foreground = HudUi.Text;
        }

        ClockUnit.Text = "GHz";
        PowerUnit.Text = s["dash.packageUnit"];
        VoltUnit.Text = "V";
        CpuRing.Set(0, "CPU", "—", "");
        _clock.Tick += (_, _) => ShowClock();
        ShowClock();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Monitor.Updated += OnUpdated;
        _clock.Start();
        if (App.Monitor.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Monitor.Updated -= OnUpdated;
        _clock.Stop();
        base.OnNavigatedFrom(e);
    }

    private void OnUpdated(object? sender, SensorSnapshot snapshot)
    {
        if (App.MainWindow is { IsShown: false })
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => Apply(snapshot));
    }

    private void ShowClock()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText.Text = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up:hh\\:mm\\:ss}" : up.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        IntervalText.Text = $"{App.Monitor.Interval.TotalMilliseconds.ToString("N0", App.Text.Culture)} ms";
    }

    private void Apply(SensorSnapshot snapshot)
    {
        var s = App.Text;
        var c = s.Culture;
        var d = Dashboard.From(snapshot, App.Settings.Current.PumpSensorId);
        string N(double? v, string format = "0") => v is { } x ? x.ToString(format, c) : "—";

        if (snapshot.Readings.FirstOrDefault(r => r.Parent is not null)?.Parent is { } board)
        {
            BoardName.Text = board.ToUpper(c);
        }

        // Alerts: readings beyond a rule right now.
        IReadOnlyList<AlertRule> rules = App.Settings.Current.AlertsEnabled ? App.Settings.EffectiveRules : [];
        var alerts = snapshot.Readings.Count(r => AlertEvaluator.IsBeyond(r, rules));
        var stateBrush = alerts > 0 ? HudUi.Crit : HudUi.Accent;
        StatusText.Text = (alerts > 0 ? s.Format("dash.alerts", alerts) : s["dash.nominal"]).ToUpper(c);
        StatusText.Foreground = stateBrush;
        StatusDot.Fill = stateBrush;
        StatusChip.BorderBrush = stateBrush;

        // Processor
        var cpu = d.Cpu;
        CpuName.Text = (cpu.Name ?? "—").ToUpper(c);
        var cpuHot = cpu.Temperature >= 85;
        CpuRing.Set(
            (cpu.Temperature ?? cpu.Load ?? 0) / 100,
            cpu.Temperature is null ? s["dash.load"].ToUpper(c) : "CPU · TEMP",
            cpu.Temperature is { } t ? $"{t:0}°" : cpu.Load is { } l ? $"{l:0}%" : "—",
            cpu.Load is { } load ? s.Format("dash.loadValue", N(load)).ToUpper(c) : "",
            cpu.Temperature is null ? null : (cpu.Load ?? 0) / 100,
            cpuHot);
        ClockValue.Text.Text = N(cpu.ClockMhz / 1000, "0.00");
        PowerValue.Text.Text = N(cpu.PowerW, "0.0");
        VoltValue.Text.Text = N(cpu.CoreVolts, "0.000");
        ThreadBars.Set(cpu.ThreadLoads);
        ThreadsCount.Text = cpu.ThreadLoads.Count > 0 ? $"{cpu.ThreadLoads.Count} T" : "";

        // Graphics card
        var gpu = d.Gpu;
        GpuName.Text = (gpu.Name ?? "—").ToUpper(c);
        GpuTemps.Text = gpu.HotSpot is { } hs ? $"{N(gpu.Temperature)}° · HS {hs:0}°" : gpu.Temperature is { } gt ? $"{gt:0}°" : "";
        GpuClock.Text.Text = gpu.ClockMhz is { } mhz ? $"{mhz.ToString("N0", c)} MHz" : "—";
        GpuPower.Text = gpu.PowerW is { } gw ? $"{gw.ToString("0", c)} W" : "";
        GpuBar.Value = (gpu.Load ?? 0) / 100;
        GpuLoad.Text = s.Format("dash.loadValue", N(gpu.Load)).ToUpper(c);
        GpuVram.Text = gpu.MemoryUsedMb is { } used && gpu.MemoryTotalMb is { } total
            ? $"VRAM {(used / 1024).ToString("0.0", c)} / {(total / 1024).ToString("0", c)} GB"
            : "";

        // Memory
        var mem = d.Memory;
        MemUsed.Text.Text = mem.UsedGb is { } u && mem.TotalGb is { } tot ? $"{u.ToString("0.0", c)} / {tot.ToString("0.0", c)} GB" : "—";
        MemPercent.Text = mem.Load is { } ml ? $"{ml:0}%" : "";
        MemBar.Value = (mem.Load ?? 0) / 100;
        MemBar.Fill = mem.Load >= 90 ? HudUi.Warn : null;
        MemFree.Text = mem.TotalGb is { } all && mem.UsedGb is { } inUse ? s.Format("dash.free", (all - inUse).ToString("0.0", c)).ToUpper(c) : "";

        // Temperature history
        var series = new List<ChartSeries>();
        if (cpu.TemperatureId is { } cpuId)
        {
            series.Add(new ChartSeries(App.Monitor.History.Get(cpuId), HudUi.Accent, Fill: true));
        }

        if (gpu.TemperatureId is { } gpuId)
        {
            series.Add(new ChartSeries(App.Monitor.History.Get(gpuId), HudUi.Warn));
        }

        TempChart.Capacity = App.Monitor.History.Capacity;
        TempChart.Set(series);

        // Cooling and power supply
        CoolingList.Children.Clear();
        foreach (var fan in d.Fans.Take(5))
        {
            CoolingList.Children.Add(HudUi.FactRow(fan.Name.ToUpper(c), HudUi.Mono($"{fan.Rpm.ToString("N0", c)} RPM", HudUi.Accent)));
        }

        foreach (var (rail, volts) in d.Rails)
        {
            CoolingList.Children.Add(HudUi.FactRow(rail.Name, HudUi.Mono($"{volts.ToString("0.00", c)} V", rail.Within(volts) ? HudUi.Accent : HudUi.Crit)));
        }

        if (CoolingList.Children.Count == 0)
        {
            CoolingList.Children.Add(new TextBlock { Text = s["dash.noBoardSensors"], Foreground = HudUi.TextDim, TextWrapping = TextWrapping.Wrap });
        }

        ShowDrives(d.Drives);
    }

    private readonly List<(TextBlock Facts, SegmentBar Bar)> _driveCells = [];
    private string _driveNames = "";

    private void ShowDrives(IReadOnlyList<DriveReadout> drives)
    {
        var names = string.Join("|", drives.Select(x => x.Name));
        if (names == _driveNames && _driveCells.Count == drives.Count)
        {
            for (var i = 0; i < drives.Count; i++)
            {
                var (facts, bar) = _driveCells[i];
                var warnNow = drives[i].Temperature >= 60 || drives[i].UsedPercent >= 90;
                facts.Text = DriveFacts(drives[i]);
                facts.Foreground = warnNow ? HudUi.Warn : HudUi.TextDim;
                bar.Value = (drives[i].UsedPercent ?? 0) / 100;
                bar.Fill = warnNow ? HudUi.Warn : null;
            }

            return;
        }

        _driveNames = names;
        _driveCells.Clear();
        var c = App.Text.Culture;
        const int Columns = 3;
        DriveGrid.Children.Clear();
        DriveGrid.ColumnDefinitions.Clear();
        DriveGrid.RowDefinitions.Clear();
        for (var i = 0; i < Columns; i++)
        {
            DriveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < drives.Count; i++)
        {
            if (i % Columns == 0)
            {
                DriveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var drive = drives[i];
            var warn = drive.Temperature >= 60 || drive.UsedPercent >= 90;
            var cell = new StackPanel { Spacing = 6 };
            var line = new Grid();
            line.Children.Add(new TextBlock { Text = drive.Name.ToUpper(c), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.Text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 110, 0) });
            var factsText = new TextBlock { Text = DriveFacts(drive), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = warn ? HudUi.Warn : HudUi.TextDim, HorizontalAlignment = HorizontalAlignment.Right };
            line.Children.Add(factsText);
            cell.Children.Add(line);
            var segments = new SegmentBar { Value = (drive.UsedPercent ?? 0) / 100, Fill = warn ? HudUi.Warn : null, Height = 10 };
            cell.Children.Add(segments);
            _driveCells.Add((factsText, segments));
            Grid.SetRow(cell, i / Columns);
            Grid.SetColumn(cell, i % Columns);
            DriveGrid.Children.Add(cell);
        }

        if (drives.Count == 0)
        {
            DriveGrid.Children.Add(new TextBlock { Text = App.Text["dash.noDrives"], Foreground = HudUi.TextDim });
        }
    }

    private static string DriveFacts(DriveReadout drive) => string.Join(" · ", new[]
    {
        drive.Temperature is { } t ? $"{t:0}°" : null,
        drive.UsedPercent is { } u ? $"{u:0}%" : null,
    }.OfType<string>());
}
