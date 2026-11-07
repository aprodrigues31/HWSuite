using HWSuite.Core.Benchmark;
using HWSuite.Core.Network;
using HWSuite.Gui.Hud;
using HWSuite.Providers.Windows;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Wi-Fi test: the connection (signal, band, channel, width, standard, rates, security, driver), how busy each channel
/// is around here, a stability test (pings to the router and to the Internet with the signal) and the tips.
/// </summary>
public sealed partial class WifiPage : Page
{
    private static readonly int[] Durations = [30, 60, 120, 300];

    private WifiStatus? _status;
    private WifiStability? _stability;
    private CancellationTokenSource? _run;
    private bool _scanned;

    public WifiPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = s["wifi.tag"].ToUpper(c);
        PageTitle.Text = s["wifi.title"].ToUpper(c);
        Subtitle.Text = s["wifi.subtitle"];
        ScanButton.Content = s["wifi.scan"];
        ConnectionTag.Text = ("// " + s["wifi.connection"]).ToUpper(c);
        StabilityTag.Text = ("// " + s["wifi.stability"]).ToUpper(c);
        StabilityHint.Text = s["wifi.stability.hint"];
        ChannelsTag.Text = ("// " + s["wifi.channels"]).ToUpper(c);
        ChannelsHint.Text = s["wifi.channels.hint"];
        TipsTag.Text = ("// " + s["wifi.tips"]).ToUpper(c);
        ChartLabel.Text = s["wifi.chart"].ToUpper(c);
        StartButton.Content = s["wifi.start"];
        foreach (var seconds in Durations)
        {
            DurationBox.Items.Add(seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min");
        }

        DurationBox.SelectedIndex = 1;
        BlockedBar.Message = s["wifi.blocked"];
        BlockedBar.IsOpen = false;
        Loaded += async (_, _) =>
        {
            if (!_scanned)
            {
                _scanned = true;
                await ScanAsync();
            }
        };
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        var s = App.Text;
        ScanButton.IsEnabled = false;
        ScanStatus.Text = s["wifi.scanning"];
        try
        {
            _status = App.IsDemo ? Demo() : await Task.Run(() => WlanReader.Status(scan: true));
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }

        ScanStatus.Text = _status is null ? s["wifi.none"] : "";
        StartButton.IsEnabled = _status is { Connected: true } && (_status.Gateway is not null || App.IsDemo);
        Show();
    }

    private void Show()
    {
        var s = App.Text;
        var c = s.Culture;
        ConnectionRows.Children.Clear();
        ChannelRows.Children.Clear();
        NetworkRows.Children.Clear();
        TipRows.Children.Clear();
        BlockedBar.IsOpen = _status?.ScanBlocked == true;
        if (_status is not { } w)
        {
            NetworksTag.Text = ("// " + s.Format("wifi.networks", 0)).ToUpper(c);
            Panels.Reflow();
            return;
        }

        foreach (var (label, value) in WifiText.Connection(w, s))
        {
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 300, TextAlignment = TextAlignment.Right };
            if (label == s["wifi.signal"] && w.Rssi is { } rssi)
            {
                text.Foreground = rssi >= WifiAnalysis.FairRssi ? HudUi.Good : rssi >= WifiAnalysis.WeakRssi ? HudUi.Warn : HudUi.Crit;
            }

            ConnectionRows.Children.Add(HudUi.FactRow(label, text));
        }

        // Channels, per band heard.
        foreach (var band in new[] { 2.4, 5, 6 }.Where(b => w.Networks.Any(n => n.BandGhz == b) || w.BandGhz == b))
        {
            var loads = WifiAnalysis.Channels(w.Networks, band, w.Ssid);
            var shown = band == 2.4 ? loads : loads.Where(l => l.Networks > 0 || (w.BandGhz == band && l.Channel == w.Channel)).ToList();
            var best = WifiAnalysis.Best(loads);
            var group = new StackPanel { Spacing = 4 };
            group.Children.Add(new TextBlock
            {
                Text = $"{band.ToString("0.#", c)} GHz" + (best is null ? "" : " · " + s.Format("wifi.channels.best", best.Channel)),
                FontFamily = HudUi.Font("HudMonoStrongFont"),
                FontSize = 13,
            });
            var max = Math.Max(3, loads.Max(l => l.Score));
            foreach (var load in shown)
            {
                group.Children.Add(ChannelRow(load, max, w.BandGhz == band && w.Channel == load.Channel, best?.Channel == load.Channel));
            }

            if (shown.Count == 0 && best is not null)
            {
                group.Children.Add(ChannelRow(best, max, false, true));
            }

            ChannelRows.Children.Add(group);
        }

        // Networks around.
        NetworksTag.Text = ("// " + s.Format("wifi.networks", w.Networks.Count)).ToUpper(c);
        foreach (var n in w.Networks.Take(30))
        {
            var name = new TextBlock
            {
                Text = (n.Connected ? "● " : "") + (n.Ssid ?? s["wifi.networks.hidden"]),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = n.Connected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = n.Connected ? HudUi.Accent : HudUi.Text,
            };
            var detail = new TextBlock
            {
                Text = $"{n.BandGhz?.ToString("0.#", c)} GHz · ch {n.Channel} · {n.WidthMhz} MHz · {n.Rssi} dBm",
                FontFamily = HudUi.Font("HudMonoFont"),
                FontSize = 12,
                Foreground = n.Rssi >= WifiAnalysis.FairRssi ? HudUi.Text : HudUi.TextDim,
            };
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(name);
            Grid.SetColumn(detail, 1);
            row.Children.Add(detail);
            if (n.Standard is { } std)
            {
                ToolTipService.SetToolTip(row, $"{n.Bssid} · {std}");
            }

            NetworkRows.Children.Add(row);
        }

        ShowTips();
        Panels.Reflow();
    }

    private static Grid ChannelRow(ChannelLoad load, double max, bool mine, bool best)
    {
        var s = App.Text;
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.Children.Add(new TextBlock
        {
            Text = $"CH {load.Channel}" + (load.Dfs ? "*" : ""),
            FontFamily = HudUi.Font("HudMonoFont"),
            FontSize = 12,
            Foreground = mine ? HudUi.Accent : best ? HudUi.Good : HudUi.TextDim,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var bar = new SegmentBar { Height = 10, Value = Math.Clamp(load.Score / max, 0, 1), VerticalAlignment = VerticalAlignment.Center };
        if (load.Score >= 4)
        {
            bar.Fill = HudUi.Crit;
        }
        else if (load.Score >= 2)
        {
            bar.Fill = HudUi.Warn;
        }

        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var count = new TextBlock { Text = s.Format("wifi.channels.networks", load.Networks), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.TextDim, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(count, 2);
        row.Children.Add(count);
        if (mine)
        {
            var you = new TextBlock { Text = s["wifi.channels.yours"], FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 11, Foreground = HudUi.Accent, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(you, 3);
            row.Children.Add(you);
        }

        return row;
    }

    private void ShowTips()
    {
        if (_status is not { } w)
        {
            return;
        }

        var s = App.Text;
        var advice = WifiAnalysis.Advice(w, _stability, DateTime.Today);
        var number = 1;
        foreach (var tip in advice)
        {
            var args = WifiText.Args(tip, s.Culture);
            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Top,
                Background = tip.Level switch
                {
                    AdviceLevel.Problem => HudUi.Crit,
                    AdviceLevel.Good => HudUi.Good,
                    _ => HudUi.Warn,
                },
                Child = new TextBlock
                {
                    Text = tip.Level == AdviceLevel.Good ? "✓" : number++.ToString(s.Culture),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = HudUi.OnFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = s.Format(tip.Key + ".title", args), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = s.Format(tip.Key + ".body", args), TextWrapping = TextWrapping.Wrap, Foreground = HudUi.TextDim });
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(badge);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            TipRows.Children.Add(row);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var c = s.Culture;
        if (_run is not null)
        {
            _run.Cancel();
            return;
        }

        if (_status is not { Connected: true } w)
        {
            return;
        }

        var seconds = Durations[Math.Max(0, DurationBox.SelectedIndex)];
        var gw = new List<double>();
        var net = new List<double>();
        _run = new CancellationTokenSource();
        StartButton.Content = s["wifi.stop"];
        ScanButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        ChartLabel.Visibility = Visibility.Visible;
        LatencyChart.Visibility = Visibility.Visible;
        LatencyChart.Capacity = seconds;
        StabilityLines.Children.Clear();
        var progress = new Progress<WifiSample>(x =>
        {
            Progress.Value = x.Seconds / seconds;
            StabilityStatus.Text = s.Format("wifi.running", x.Seconds.ToString("0", c), seconds) + (x.Rssi is { } r ? $" · {r} dBm" : "");
            gw.Add(x.GatewayMs ?? 0);
            net.Add(x.InternetMs ?? 0);
            LatencyChart.Set([new ChartSeries([.. gw], HudUi.Accent, Fill: true), new ChartSeries([.. net], HudUi.Warn)]);
        });
        try
        {
            _stability = App.IsDemo
                ? await DemoStability(seconds, progress, _run.Token)
                : await WifiAnalysis.RunStabilityAsync(() => WlanReader.Link(w.InterfaceId), w.Gateway!, "1.1.1.1", TimeSpan.FromSeconds(seconds), progress, _run.Token);
            StabilityStatus.Text = "";
            foreach (var line in WifiText.Stability(_stability, s))
            {
                StabilityLines.Children.Add(new TextBlock { Text = line, FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }

            var advice = WifiAnalysis.Advice(w, _stability, DateTime.Today);
            try
            {
                WifiSummary.Save(BenchmarkFiles.Wifi, WifiSummary.From(w, _stability, advice, DateTimeOffset.Now));
                StabilityLines.Children.Add(new TextBlock { Text = s["wifi.saved"], FontSize = 12, Foreground = HudUi.TextDim });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // not kept
            }

            TipRows.Children.Clear();
            ShowTips();
        }
        finally
        {
            _run.Dispose();
            _run = null;
            StartButton.Content = s["wifi.start"];
            ScanButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
            Panels.Reflow();
        }
    }

    // ---- Demo (CI screenshots) ------------------------------------------------------------------------------------

    private static WifiStatus Demo()
    {
        WifiBss N(string? ssid, string bssid, int rssi, int mhz, int width, bool me = false) =>
            new(ssid, bssid, rssi, mhz, WifiMath.Channel(mhz), WifiMath.Band(mhz), mhz > 5000 ? "802.11ax (Wi-Fi 6)" : "802.11n (Wi-Fi 4)", width, me);
        List<WifiBss> networks =
        [
            N("Casa-2G", "A0:B1:C2:00:00:01", -48, 2437, 40, true),
            N("Casa-5G", "A0:B1:C2:00:00:02", -55, 5180, 80),
            N("Vizinho_101", "10:20:30:00:00:01", -66, 2437, 20),
            N("NET_2G4A1B", "10:20:30:00:00:02", -71, 2437, 40),
            N("Apto 32", "10:20:30:00:00:03", -63, 2432, 20),
            N("VIVOFIBRA-88", "10:20:30:00:00:04", -74, 2412, 20),
            N("CLARO_2G", "10:20:30:00:00:05", -79, 2462, 20),
            N(null, "10:20:30:00:00:06", -82, 2412, 20),
            N("VIVOFIBRA-88-5G", "10:20:30:00:00:07", -77, 5180, 80),
            N("Apto 32 5G", "10:20:30:00:00:08", -70, 5745, 80),
        ];
        return new WifiStatus("demo", "Intel(R) Wi-Fi 6 AX200 160MHz", "23.60.0.10", new DateTime(2022, 3, 14), "Intel", true, "Casa-2G", "A0:B1:C2:00:00:01",
            96, -48, "802.11n (Wi-Fi 4)", 2.4, 6, 40, 144, 144, "WPA2-PSK", "AES (CCMP)", "192.168.0.1", networks, false);
    }

    private static async Task<WifiStability> DemoStability(int seconds, IProgress<WifiSample> progress, CancellationToken cancel)
    {
        var samples = new List<WifiSample>();
        var random = new Random(7);
        var count = Math.Min(seconds, 20);
        for (var i = 0; i < count && !cancel.IsCancellationRequested; i++)
        {
            var sample = new WifiSample(i, -48 - random.Next(0, 6), "A0:B1:C2:00:00:01", 144, 2 + (random.NextDouble() * 3), 9 + (random.NextDouble() * 6));
            samples.Add(sample);
            progress.Report(sample);
            await Task.Delay(50, CancellationToken.None);
        }

        return WifiStability.From(samples, "192.168.0.1", "1.1.1.1", TimeSpan.FromSeconds(count));
    }
}
