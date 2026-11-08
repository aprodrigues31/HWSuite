using System.Globalization;
using System.Runtime.InteropServices;
using HWSuite.Core.Alerts;
using HWSuite.Core.Network;
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
    private static LiveNetworkMonitor? s_net;
    private readonly DashboardNetwork _network = new();
    private readonly IDashboardView? _view = DashboardViews.For(HudThemes.Current.Id);

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
        ClockLabel.Text = s["dash.maxTemp"].ToUpper(c);
        GpuPowerLabel.Text = s["dash.power"].ToUpper(c);
        GpuFanLabel.Text = s["dash.fan"].ToUpper(c);
        GpuMemLabel.Text = s["dash.memory"].ToUpper(c);
        GpuPowerUnit.Text = s["dash.boardUnit"];
        GpuFanUnit.Text = "RPM";
        GpuMemUnit.Text = "MHz";
        PowerLabel.Text = s["dash.power"].ToUpper(c);
        VoltLabel.Text = "VCORE";
        ThreadsLabel.Text = s["dash.threads"].ToUpper(c);
        ChartTag.Text = ("// " + s.Format("dash.temps", s.Format("dash.minutes", App.Monitor.History.Capacity * App.Monitor.Interval.TotalMinutes))).ToUpper(c);
        CoolingTag.Text = ("// " + s["dash.cooling"]).ToUpper(c);
        StorageTag.Text = ("// " + s["dash.storage"]).ToUpper(c);
        MachineText.Text = $"{Environment.MachineName} · {(App.IsDemo ? "Windows 11 Pro 25H2 · build 26200.7171 · x64" : HWSuite.Providers.Windows.WindowsOsProvider.Describe() ?? RuntimeInformation.OSDescription)}";
        if (_view is null)
        {
            NetSlot.Content = _network.Root;
            NetBody.Visibility = Visibility.Collapsed;
        }
        else
        {
            NetBody.Content = _network.Root;
        }

        SizeChanged += (_, e) => Fit(e.NewSize);
        LayoutUpdated += (_, _) =>
        {
            if (!_fitted && ActualWidth > 0)
            {
                _fitted = true;
                Fit(new Windows.Foundation.Size(ActualWidth, ActualHeight));
            }

            FitHeight();
        };
        BoardName.Text = Environment.MachineName.ToUpper(c);
        StatusText.Text = s["dash.waiting"].ToUpper(c);
        if (_view is not null)
        {
            HudBody.Visibility = Visibility.Collapsed;
            AltBody.Content = _view.Root;
            AltBody.Visibility = Visibility.Visible;
        }

        foreach (var value in new[] { ClockValue, PowerValue, VoltValue, GpuPowerValue, GpuFanValue, GpuMemValue })
        {
            value.Text.Style = HudUi.Style("HudValueStyle");
            value.Text.Foreground = HudUi.Accent;
        }

        foreach (var value in new[] { MemUsed, VramUsed })
        {
            value.Text.FontFamily = HudUi.Font("HudMonoStrongFont");
            value.Text.FontSize = 22;
            value.Text.Foreground = HudUi.Text;
        }

        MemLabel.Text = "RAM";
        VramLabel.Text = "VRAM";

        ClockUnit.Text = s["dash.session"];
        PowerUnit.Text = s["dash.packageUnit"];
        VoltUnit.Text = "V";
        CpuRing.Set(0, "TEMP", "—", "");
        CpuLoadRing.Set(0, s["dash.load"].ToUpper(c), "—", "");
        CpuClockRing.Set(0, "CLOCK", "—", "");
        GpuTempRing.Set(0, "TEMP", "—", "");
        GpuLoadRing.Set(0, s["dash.load"].ToUpper(c), "—", "");
        GpuClockRing.Set(0, "CLOCK", "—", "");
        _clock.Tick += (_, _) => ShowClock();
        ShowClock();
    }

    /// <summary>"DDR4 · 3600 MT/s", read once from SMBIOS (quick, no driver needed).</summary>
    private static string? s_ramLabel;
    private static bool s_ramLabelAsked;

    private static void ReadRamLabel()
    {
        if (s_ramLabelAsked)
        {
            return;
        }

        s_ramLabelAsked = true;
        if (App.IsDemo)
        {
            s_ramLabel = "DDR4 · 3600 MT/s";
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var smbios = HWSuite.Hosting.ProviderCatalog.Default().Where(p => p.Name == "SMBIOS");
                s_ramLabel = MemoryReadout.Label(new HWSuite.Core.Collection.InventoryCollector(smbios).Collect().Memory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // no label
            }
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ReadRamLabel();
        App.Monitor.Updated += OnUpdated;
        _clock.Start();
        if (App.IsDemo)
        {
            _network.Update(DashboardNetwork.Demo());
        }
        else
        {
            s_net ??= new LiveNetworkMonitor(wifi: OperatingSystem.IsWindows() ? HWSuite.Providers.Windows.WlanReader.Query : null);
            s_net.Updated += OnNetwork;
            s_net.Start();
            _network.Update(s_net.Snapshot());
        }
        if (App.Monitor.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Monitor.Updated -= OnUpdated;
        _clock.Stop();
        if (s_net is not null)
        {
            s_net.Updated -= OnNetwork;
            s_net.Stop();
        }
        base.OnNavigatedFrom(e);
    }

    private bool _fitted;

    /// <summary>
    /// Fits the dashboard to the window: panel rows wrap when narrow, the processor ring and the temperature chart
    /// grow on large screens.
    /// </summary>
    private double _scale = 1;

    /// <summary>
    /// Fits the dashboard to the window it is in, whatever the screen (HD to 4K): a zoom so it is not tiny on large
    /// screens (1× up to a 1820 × 1000 window, growing with the smaller ratio, at most 2×), then FitHeight.
    /// </summary>
    private void Fit(Windows.Foundation.Size size)
    {
        var vw = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : size.Width;
        var vh = Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : size.Height;
        if (vw <= 0 || vh <= 0)
        {
            return;
        }

        _scale = Math.Clamp(Math.Min(vw / 1820, vh / 1000), 1, 2);
        Body.Width = Math.Floor(vw / _scale) - 1;
        var w = Body.Width;
        var h = vh / _scale;
        SizeDials(w, h);
        TempChart.Height = Math.Clamp(h * 0.2, 100, 300);
        _network.PingHeight = TempChart.Height;
        _fitPasses = 0;
    }

    private int _fitPasses;
    private double _maxClock = 4000;

    /// <summary>
    /// The three CPU dials share about half of the processor panel's width (the rest is name, power, voltage and
    /// threads), so they keep the same proportion on HD, Full HD, 2K and 4K.
    /// </summary>
    private void SizeDials(double bodyWidth, double height)
    {
        // Each block is half the body when side by side, the whole body when stacked; three dials share its width.
        var panel = (TopRow.ColumnDefinitions.Count == 2 ? (bodyWidth - 56 - 14) / 2 : bodyWidth - 56) - 46;
        var d = Math.Round(Math.Clamp(Math.Min(((panel - 16) / 3) * 0.8, height * 0.19), 110, 220) / 2) * 2;
        CpuLoadRing.Diameter = CpuRing.Diameter = CpuClockRing.Diameter = d;
        GpuLoadRing.Diameter = GpuTempRing.Diameter = GpuClockRing.Diameter = d;
    }

    private IReadOnlyList<DriveReadout> _drives = [];
    private double _maxCpuTemp;
    private double _maxGpuClock = 1000;

    /// <summary>
    /// Makes the dashboard fit the window height (no scrolling on Full HD and up): the temperature chart, then the
    /// processor ring, give up height until nothing is left below the fold. Only where that is not enough (very
    /// small windows) the page scrolls.
    /// </summary>
    private void FitHeight()
    {
        if (_fitPasses >= 20 || Scroller.ViewportHeight <= 0 || Body.ActualHeight <= 0)
        {
            return;
        }

        _fitPasses++;
        if (_view is not null)
        {
            _fitPasses = 20;
            return;
        }

        // Positive: taller than the window (shrink); negative: room left (grow), in dashboard pixels.
        var overflow = Body.ActualHeight - (Scroller.ViewportHeight / _scale) + 2;
        if (Math.Abs(overflow) <= 3)
        {
            _fitPasses = 20;
            return;
        }

        if (overflow > 0)
        {
            var chart = Math.Max(90, TempChart.Height - overflow);
            if (chart < TempChart.Height - 1)
            {
                TempChart.Height = chart;
                _network.PingHeight = chart;
                return;
            }

            // Then the six dials give up height, down to a readable minimum.
            var dial = Math.Max(110, Math.Floor((CpuRing.Diameter - overflow) / 2) * 2);
            if (dial < CpuRing.Diameter)
            {
                CpuLoadRing.Diameter = CpuRing.Diameter = CpuClockRing.Diameter = dial;
                GpuLoadRing.Diameter = GpuTempRing.Diameter = GpuClockRing.Diameter = dial;
                return;
            }

            _fitPasses = 20; // as small as it goes: the page scrolls
            return;
        }

        // Room left: the chart takes it (up to a limit), so the page ends at the bottom of the window.
        var grown = Math.Min(700, TempChart.Height - overflow);
        if (grown > TempChart.Height + 1)
        {
            TempChart.Height = grown;
            _network.PingHeight = grown;
            return;
        }

        _fitPasses = 20;
    }

    private void OnNetwork(object? sender, LiveNetworkReading reading) =>
        DispatcherQueue.TryEnqueue(() => _network.Update(reading));

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
        if (_view is not null)
        {
            _view.Update(d, snapshot);
            return;
        }

        // Processor
        var cpu = d.Cpu;
        CpuName.Text = (cpu.Name ?? "—").ToUpper(c);
        var cpuHot = cpu.Temperature >= 85;
        CpuRing.Set((cpu.Temperature ?? 0) / 100, "TEMP", cpu.Temperature is { } t ? $"{t:0}°" : "—", "CPU", null, cpuHot);
        CpuLoadRing.Set((cpu.Load ?? 0) / 100, s["dash.load"].ToUpper(c), cpu.Load is { } l ? $"{l:0}%" : "—", "CPU", null, cpu.Load >= 95);
        _maxClock = Math.Max(_maxClock, cpu.ClockMhz ?? 0);
        CpuClockRing.Set((cpu.ClockMhz ?? 0) / _maxClock, "CLOCK", N(cpu.ClockMhz / 1000, "0.00"), "GHz");
        _maxCpuTemp = Math.Max(_maxCpuTemp, cpu.Temperature ?? 0);
        ClockValue.Text.Text = _maxCpuTemp > 0 ? $"{_maxCpuTemp:0}°" : "—";
        PowerValue.Text.Text = N(cpu.PowerW, "0.0");
        VoltValue.Text.Text = N(cpu.CoreVolts, "0.000");
        ThreadBars.Set(cpu.ThreadLoads);
        ThreadsCount.Text = cpu.ThreadLoads.Count > 0 ? $"{cpu.ThreadLoads.Count} T" : "";

        // Graphics card
        var gpu = d.Gpu;
        GpuName.Text = (gpu.Name ?? "—").ToUpper(c);
        GpuTemps.Text = gpu.HotSpot is { } hs ? $"{N(gpu.Temperature)}° · HS {hs:0}°" : gpu.Temperature is { } gt ? $"{gt:0}°" : "";
        GpuLoadRing.Set((gpu.Load ?? 0) / 100, s["dash.load"].ToUpper(c), gpu.Load is { } gl ? $"{gl:0}%" : "—", "GPU", null, gpu.Load >= 95);
        GpuTempRing.Set((gpu.Temperature ?? 0) / 100, "TEMP", gpu.Temperature is { } gtt ? $"{gtt:0}°" : "—", gpu.HotSpot is { } hot ? $"HS {hot:0}°" : "GPU", null, gpu.HotSpot >= 95 || gpu.Temperature >= 85);
        _maxGpuClock = Math.Max(_maxGpuClock, gpu.ClockMhz ?? 0);
        GpuClockRing.Set((gpu.ClockMhz ?? 0) / _maxGpuClock, "CLOCK", gpu.ClockMhz is { } mhz ? mhz.ToString("N0", c) : "—", "MHz");
        GpuPowerValue.Text.Text = N(gpu.PowerW, "0");
        var gpuFan = d.Fans.FirstOrDefault(f => f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase));
        GpuFanValue.Text.Text = gpuFan is null ? "—" : gpuFan.Rpm.ToString("N0", c);
        var memClock = DashboardViews.Find(snapshot, "GPU", SensorKind.Clock, "Memory");
        var memClockValue = memClock is null ? null : snapshot.Readings.FirstOrDefault(r => r.Id == memClock)?.Value;
        GpuMemValue.Text.Text = memClockValue is { } mc && memClock != DashboardViews.Find(snapshot, "GPU", SensorKind.Clock, "GPU Core", "Core") ? mc.ToString("N0", c) : "—";
        var vramPct = gpu.MemoryUsedMb is { } vmu && gpu.MemoryTotalMb is > 0 and var vmt ? vmu / vmt * 100 : (double?)null;
        GpuBar.Value = Math.Clamp((vramPct ?? 0) / 100, 0, 1);
        GpuVramText.Text = gpu.MemoryUsedMb is { } vu2 && gpu.MemoryTotalMb is { } vt2 ? $"{(vu2 / 1024).ToString("0.0", c)} / {(vt2 / 1024).ToString("0.0", c)} GB · {vramPct ?? 0:0}%" : "";
        // Memory: system RAM, then the graphics card's VRAM (never added together: they are separate memories).
        var mem = d.Memory;
        ShowMemory(MemUsed, MemPercent, MemBar, MemFree, mem.UsedGb, mem.TotalGb, mem.Load);
        MemNote.Text = (s_ramLabel ?? "").ToUpper(c);
        var vramUsed = gpu.MemoryUsedMb / 1024;
        var vramTotal = gpu.MemoryTotalMb / 1024;
        ShowMemory(VramUsed, VramPercent, VramBar, VramFree, vramUsed, vramTotal,
            vramUsed is { } vu && vramTotal is > 0 and var vt ? vu / vt * 100 : null);
        VramNote.Text = (gpu.Name is { } gn ? MemoryReadout.ShortGpuName(gn) : "").ToUpper(c);

        void ShowMemory(GlowText value, TextBlock percent, SegmentBar bar, TextBlock free, double? usedGb, double? totalGb, double? load)
        {
            value.Text.Text = usedGb is { } u && totalGb is { } tot ? $"{u.ToString("0.0", c)} / {tot.ToString("0.0", c)} GB" : "—";
            percent.Text = load is { } p ? $"{p:0}%" : "";
            bar.Value = Math.Clamp((load ?? 0) / 100, 0, 1);
            bar.Fill = load >= 90 ? HudUi.Warn : null;
            free.Text = totalGb is { } all && usedGb is { } inUse ? s.Format("dash.free", Math.Max(0, all - inUse).ToString("0.0", c)).ToUpper(c) : "";
        }

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
        foreach (var fan in DashboardViews.BoardFans(d))
        {
            CoolingList.Children.Add(HudUi.FactRow(fan.Name.ToUpper(c), HudUi.Mono($"{fan.Rpm.ToString("N0", c)} RPM", HudUi.Accent)));
        }

        // The power supply rails on one line, to keep the panel short.
        if (d.Rails.Count > 0)
        {
            var ok = d.Rails.All(r => r.Rail.Within(r.Volts));
            CoolingList.Children.Add(HudUi.FactRow(
                string.Join(" · ", d.Rails.Select(r => r.Rail.Name)),
                HudUi.Mono(string.Join(" · ", d.Rails.Select(r => r.Volts.ToString("0.00", c))) + " V", ok ? HudUi.Accent : HudUi.Crit)));
        }

        foreach (var row in CoolingList.Children.OfType<Grid>())
        {
            row.Padding = new Thickness(0, 4, 0, 4);
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
        _drives = drives;
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
        var columns = _view is null ? 1 : 3;
        DriveGrid.Children.Clear();
        DriveGrid.ColumnDefinitions.Clear();
        DriveGrid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            DriveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < drives.Count; i++)
        {
            if (i % columns == 0)
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
            Grid.SetRow(cell, i / columns);
            Grid.SetColumn(cell, i % columns);
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
