using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Network;
using HWSuite.Providers.Windows;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Internet / Wi-Fi test: the connection in use (adapter, Wi-Fi signal, band, link rate), then a speed test
/// against Cloudflare or M-Lab with three views of the result: the numbers, the per-sample charts and the
/// improvement tips that follow from them.
/// </summary>
public sealed partial class NetworkPage : Page
{
    private static readonly Brush UploadBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0xE8, 0xA8, 0x00));
    private CancellationTokenSource? _running;
    private NetworkTestResult? _result;
    private string _view = "result";

    public NetworkPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["net.title"];
        Subtitle.Text = s["net.subtitle"];
        ServerBox.Header = s["net.server"];
        ServerBox.Items.Add(s["net.server.cloudflare"]);
        ServerBox.Items.Add(s["net.server.mlab"]);
        ServerBox.SelectedIndex = 0;
        ProtocolLabel.Text = s["net.protocol"];
        StartButton.Content = s["app.bench.start"];
        NoteText.Text = s["net.note"];
        ResultTab.Text = s["net.tab.result"];
        DetailTab.Text = s["net.tab.detail"];
        TipsTab.Text = s["net.tab.tips"];
        Loaded += async (_, _) => await ShowConnectionAsync();
    }

    private IpFamily Family => ProtocolBar.SelectedItem?.Tag as string == "IPv6" ? IpFamily.IPv6 : IpFamily.IPv4;

    private async void ProtocolBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (IsLoaded && _running is null)
        {
            await ShowConnectionAsync();
        }
    }

    private void ResultTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _view = sender.SelectedItem?.Tag as string ?? "result";
        ShowResult();
    }

    private async Task<NetConnection?> ShowConnectionAsync()
    {
        var family = Family;
        var connection = await Task.Run(() => ActiveConnection.Find(family, WlanReader.Query));
        ConnectionPanel.Children.Clear();
        ConnectionPanel.Children.Add(ConnectionCard(connection));
        return connection;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_running is not null)
        {
            await _running.CancelAsync();
            return;
        }

        var server = ServerBox.SelectedIndex == 1 ? NetServer.MLab : NetServer.Cloudflare;
        if (server == NetServer.MLab && !App.Settings.Current.MlabConsent && !await AskMlabConsentAsync())
        {
            return;
        }

        var family = Family;
        using var cancel = new CancellationTokenSource();
        _running = cancel;
        StartButton.Content = s["app.bench.cancel"];
        ServerBox.IsEnabled = ProtocolBar.IsEnabled = false;
        Progress.Value = 0;
        StatusText.Text = s["net.phase.prepare"];
        try
        {
            var connection = await ShowConnectionAsync();
            var progress = new Progress<NetProgress>(p =>
            {
                Progress.Value = p.Fraction;
                StatusText.Text = p.Phase switch
                {
                    "prepare" => s["net.phase.prepare"],
                    "latency" => s.Format("net.phase.latency", Number(p.Value ?? 0)),
                    "download" => s.Format("net.phase.download", Number(p.Value ?? 0)),
                    "upload" => s.Format("net.phase.upload", Number(p.Value ?? 0)),
                    _ => "",
                };
            });

            var result = await Task.Run(async () =>
            {
                using INetworkProbe probe = server == NetServer.MLab ? new MlabProbe(family) : new CloudflareProbe(family);
                return await NetworkTest.RunAsync(probe, server, family, connection, null, progress, cancel.Token);
            });

            _result = result;
            StatusText.Text = s["app.bench.savedHint"];
            try
            {
                NetworkHistory.Append(BenchmarkFiles.Network, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = ex.Message;
            }

            ResultTabs.Visibility = Visibility.Visible;
            ShowResult();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = s["bench.cancelled"];
            Progress.Value = 0;
        }
        catch (NetworkTestException ex)
        {
            StatusText.Text = s[ex.Key];
            Progress.Value = 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException)
        {
            StatusText.Text = $"{s["net.error.transfer"]} ({ex.Message})";
            Progress.Value = 0;
        }
        finally
        {
            _running = null;
            StartButton.Content = s["app.bench.start"];
            ServerBox.IsEnabled = ProtocolBar.IsEnabled = true;
        }
    }

    /// <summary>M-Lab publishes every test with the public IP: say so, link the policy, and remember the answer if asked to.</summary>
    private async Task<bool> AskMlabConsentAsync()
    {
        var s = App.Text;
        var remember = new CheckBox { Content = s["net.consent.remember"] };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = s["net.consent.body"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new HyperlinkButton { Content = s["net.consent.policy"], NavigateUri = MlabProbe.PrivacyPolicy, Padding = new Thickness(0) });
        panel.Children.Add(remember);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s["net.consent.title"],
            Content = panel,
            PrimaryButtonText = s["net.consent.accept"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        if (remember.IsChecked == true)
        {
            App.Settings.Update(x => x with { MlabConsent = true });
        }

        return true;
    }

    private void ShowResult()
    {
        ResultPanel.Children.Clear();
        if (_result is not { } r)
        {
            return;
        }

        switch (_view)
        {
            case "detail":
                ShowDetail(r);
                break;
            case "tips":
                ShowTips(r);
                break;
            default:
                ShowSummary(r);
                break;
        }
    }

    // ---- Result tab -------------------------------------------------------------------------------------------

    private void ShowSummary(NetworkTestResult r)
    {
        var s = App.Text;
        var metrics = new StackPanel { Spacing = 2 };
        metrics.Children.Add(MetricRow("", s["net.download"], null, Number(r.DownloadMbps), "Mbps"));
        metrics.Children.Add(MetricRow("", s["net.upload"], null, Number(r.UploadMbps), "Mbps"));
        metrics.Children.Add(MetricRow("", s["net.latency"], s["net.idle"], Number(r.Idle.Summary.Median), "ms"));
        metrics.Children.Add(SubRow(s["net.withDownload"], Number(r.LoadedDownload.Summary.Median), "ms"));
        metrics.Children.Add(SubRow(s["net.withUpload"], Number(r.LoadedUpload.Summary.Median), "ms"));
        metrics.Children.Add(MetricRow("", s["net.jitter"], s["net.idle"], Number(r.Idle.Jitter), "ms"));
        metrics.Children.Add(SubRow(s["net.withDownload"], Number(r.LoadedDownload.Jitter), "ms"));
        metrics.Children.Add(SubRow(s["net.withUpload"], Number(r.LoadedUpload.Jitter), "ms"));
        metrics.Children.Add(r.LossPercent is { } loss
            ? MetricRow("", s["net.loss"], s["net.idle"], loss.ToString("0.#", App.Text.Culture), "%")
            : MetricRow("", s["net.loss"], s["net.idle"], s["net.lossNa"], ""));
        ResultPanel.Children.Add(Card(s.Format("net.metrics", r.Family), metrics));

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(InfoRow(s["net.serverName"], r.Endpoint.Server ?? r.Endpoint.Host));
        if (r.Endpoint.Provider is { } provider)
        {
            info.Children.Add(InfoRow(s["net.provider"], provider));
        }

        if (r.Endpoint.ClientAddress is { } address)
        {
            info.Children.Add(InfoRow(s["net.address"], address, copy: true));
        }

        info.Children.Add(InfoRow(s["net.protocol"], r.Family.ToString()));
        if (r.Connection is { } c)
        {
            info.Children.Add(InfoRow(s["net.connection"], c.Summary));
        }

        info.Children.Add(InfoRow(s["results.when"], r.At.LocalDateTime.ToString("g", s.Culture)));
        ResultPanel.Children.Add(Card(s["net.measurement"], info));
    }

    private static Grid MetricRow(string glyph, string label, string? qualifier, string value, string unit)
    {
        var grid = Row(new Thickness(0, 8, 0, 0));
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        left.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = label });
        if (qualifier is not null)
        {
            name.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + qualifier, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") });
        }

        left.Children.Add(name);
        grid.Children.Add(left);
        grid.Children.Add(Value(value, unit, big: true));
        return grid;
    }

    private static Grid SubRow(string label, string value, string unit)
    {
        var grid = Row(new Thickness(28, 0, 0, 0));
        grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") });
        grid.Children.Add(Value(value, unit, big: false));
        return grid;
    }

    private static Grid InfoRow(string label, string value, bool copy = false)
    {
        var grid = Row(new Thickness(0, 6, 0, 6));
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush("TextFillColorSecondaryBrush") });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        right.Children.Add(new TextBlock { Text = value, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (copy)
        {
            var button = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4) };
            ToolTipService.SetToolTip(button, App.Text["net.copy"]);
            button.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);
            };
            right.Children.Add(button);
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static Grid Row(Thickness margin)
    {
        var grid = new Grid { Margin = margin, ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    /// <summary>"909 Mbps": the number bold, the unit lighter, right-aligned in column 1.</summary>
    private static TextBlock Value(string value, string unit, bool big)
    {
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = value, FontWeight = FontWeights.SemiBold, FontSize = big ? 16 : 13 });
        if (unit.Length > 0)
        {
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + unit, FontSize = big ? 14 : 12 });
        }

        Grid.SetColumn(text, 1);
        return text;
    }

    // ---- Details tab ------------------------------------------------------------------------------------------

    private void ShowDetail(NetworkTestResult r)
    {
        var s = App.Text;
        ResultPanel.Children.Add(new TextBlock { Text = s["net.detailNote"], Foreground = Brush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        ResultPanel.Children.Add(SeriesCard("", s["net.download"], r.Download, "Mbps", s["net.chart.mbps"], Brush("AccentFillColorDefaultBrush")));
        ResultPanel.Children.Add(SeriesCard("", s["net.upload"], r.Upload, "Mbps", s["net.chart.mbps"], UploadBrush));

        // Latency and jitter of the three phases one after the other on a single time axis.
        var phases = new[] { r.Idle, r.LoadedDownload, r.LoadedUpload };
        var latency = new List<NetSample>();
        var jitter = new List<NetSample>();
        var offset = 0.0;
        foreach (var phase in phases)
        {
            latency.AddRange(phase.Samples.Select(x => new NetSample(offset + x.Seconds, x.Value)));
            jitter.AddRange(phase.JitterSeries.Select(x => new NetSample(offset + x.Seconds, x.Value)));
            offset += phase.Samples.Count > 0 ? phase.Samples[^1].Seconds + 0.25 : 0;
        }

        ResultPanel.Children.Add(SeriesCard("", s["net.latency"], latency, "ms", s["net.chart.ms"], Brush("TextFillColorPrimaryBrush"), s["net.latencyPhases"], r.Idle.Summary.Median));
        ResultPanel.Children.Add(SeriesCard("", s["net.jitter"], jitter, "ms", s["net.chart.ms"], UploadBrush, s["net.latencyPhases"], r.Idle.Jitter));
    }

    private static Border SeriesCard(string glyph, string title, IReadOnlyList<NetSample> series, string unit, string axis, Brush stroke, string? note = null, double? headline = null)
    {
        var s = App.Text;
        var summary = MetricSummary.Of(series);
        var content = new StackPanel { Spacing = 10 };
        var head = Row(new Thickness(0));
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        name.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        name.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(name);
        head.Children.Add(Value(Number(headline ?? summary.Median), unit, big: true));
        content.Children.Add(head);
        if (note is not null)
        {
            content.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") });
        }

        content.Children.Add(Chart(series, axis, stroke));

        var stats = new Grid { ColumnSpacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stats.RowDefinitions.Add(new RowDefinition());
        stats.RowDefinitions.Add(new RowDefinition());
        void Stat(int row, int col, string label, double value, bool strong)
        {
            var t = new TextBlock { FontSize = 12 };
            t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = label + " ", FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal });
            t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{Number(value)} {unit}" });
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            stats.Children.Add(t);
        }

        Stat(0, 0, s["net.median"], summary.Median, strong: true);
        Stat(1, 0, s["net.mean"], summary.Mean, strong: false);
        Stat(0, 1, s["net.min"], summary.Min, strong: false);
        Stat(1, 1, s["net.max"], summary.Max, strong: false);
        content.Children.Add(stats);
        return Card(null, content);
    }

    /// <summary>A line chart of the series: value axis from 0 to the maximum, time axis in seconds; redrawn on resize.</summary>
    private static Grid Chart(IReadOnlyList<NetSample> series, string axis, Brush stroke)
    {
        const double Height = 110;
        var maxValue = series.Count == 0 ? 1 : Math.Max(series.Max(x => x.Value), 1e-6);
        var maxTime = series.Count == 0 ? 1 : Math.Max(series[^1].Seconds, 1e-6);
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Height) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var secondary = Brush("TextFillColorSecondaryBrush");
        var axisTitle = new TextBlock { Text = axis, FontSize = 11, Foreground = secondary };
        Grid.SetColumn(axisTitle, 1);
        grid.Children.Add(axisTitle);
        var top = new TextBlock { Text = Number(maxValue), FontSize = 11, Foreground = secondary, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(top, 1);
        grid.Children.Add(top);

        var canvas = new Canvas { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(canvas, 1);
        Grid.SetColumn(canvas, 1);
        grid.Children.Add(canvas);

        var times = new Grid();
        times.Children.Add(new TextBlock { Text = "0", FontSize = 11, Foreground = secondary });
        times.Children.Add(new TextBlock { Text = $"{maxTime.ToString("0", CultureInfo.InvariantCulture)} s", FontSize = 11, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetRow(times, 2);
        Grid.SetColumn(times, 1);
        grid.Children.Add(times);

        canvas.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width;
            var h = e.NewSize.Height;
            canvas.Children.Clear();
            var divider = Brush("DividerStrokeColorDefaultBrush");
            canvas.Children.Add(new Line { X1 = 0, Y1 = h - 0.5, X2 = w, Y2 = h - 0.5, Stroke = divider, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = 0.5, Y1 = 0, X2 = 0.5, Y2 = h, Stroke = divider, StrokeThickness = 1 });
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in series)
            {
                line.Points.Add(new Point(p.Seconds / maxTime * w, h - 2 - (p.Value / maxValue * (h - 4))));
            }

            canvas.Children.Add(line);
        };

        return grid;
    }

    // ---- Improvements tab -------------------------------------------------------------------------------------

    private void ShowTips(NetworkTestResult r)
    {
        var s = App.Text;
        ResultPanel.Children.Add(new TextBlock { Text = s["net.tipsIntro"], Foreground = Brush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        var number = 1;
        foreach (var tip in NetworkAdvice.For(r))
        {
            var args = tip.Args.Select(a => a is double d ? Number(d) : a).ToArray();
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            var badgeBrush = tip.Level switch
            {
                AdviceLevel.Problem => Brush("SystemFillColorCautionBrush"),
                AdviceLevel.Good => Brush("SystemFillColorSuccessBrush"),
                _ => Brush("AccentFillColorDefaultBrush"),
            };
            header.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = badgeBrush,
                Child = new TextBlock
                {
                    Text = tip.Level == AdviceLevel.Good ? "✓" : number.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = Brush("TextOnAccentFillColorPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            header.Children.Add(new TextBlock { Text = s.Format(tip.Key + ".title", args), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            ResultPanel.Children.Add(new Expander
            {
                Header = header,
                Content = new TextBlock { Text = s.Format(tip.Key + ".body", args), TextWrapping = TextWrapping.Wrap },
                IsExpanded = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            });
            number++;
        }
    }

    // ---- Connection card --------------------------------------------------------------------------------------

    private static Border ConnectionCard(NetConnection? c)
    {
        var s = App.Text;
        var rows = new StackPanel { Spacing = 0 };
        if (c is null)
        {
            rows.Children.Add(new TextBlock { Text = s["net.noConnection"], Foreground = Brush("TextFillColorSecondaryBrush") });
            return Card(s["net.connection"], rows);
        }

        rows.Children.Add(InfoRow(s["net.adapter"], c.Description.Length > 0 ? c.Description : c.Name));
        rows.Children.Add(InfoRow(s["net.type"], c.IsWifi ? s["net.type.wifi"] : c.IsEthernet ? s["net.type.ethernet"] : s["net.type.other"]));
        if (!c.IsWifi && c.LinkMbps is { } link)
        {
            rows.Children.Add(InfoRow(s["net.linkSpeed"], $"{Number(link)} Mbps"));
        }

        if (c.Wifi is { } w)
        {
            if (w.Ssid is { } ssid)
            {
                rows.Children.Add(InfoRow(s["net.ssid"], ssid));
            }

            rows.Children.Add(InfoRow(s["net.signal"], w.RssiDbm is { } rssi ? s.Format("net.signalValue", w.SignalPercent, rssi) : $"{w.SignalPercent}%"));
            if (w.Standard is { } standard)
            {
                rows.Children.Add(InfoRow(s["net.standard"], standard));
            }

            if (w.BandGhz is { } band)
            {
                rows.Children.Add(InfoRow(s["net.band"], s.Format("net.bandValue", band.ToString("0.#", s.Culture), w.Channel?.ToString(CultureInfo.InvariantCulture) ?? "?")));
            }

            if (w.RxMbps is { } rx)
            {
                rows.Children.Add(InfoRow(s["net.rates"], $"{Number(rx)} / {Number(w.TxMbps ?? 0)} Mbps"));
            }
        }

        return Card(s["net.connection"], rows);
    }

    // ---- Shared ------------------------------------------------------------------------------------------------

    private static Border Card(string? title, UIElement content)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (title is not null)
        {
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        }

        panel.Children.Add(content);
        return new Border
        {
            Child = panel,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        };
    }

    /// <summary>909, 7, 1.4: whole numbers from 10 up, one decimal below.</summary>
    private static string Number(double value) =>
        value.ToString(Math.Abs(value) >= 10 || value == Math.Round(value) ? "N0" : "0.#", App.Text.Culture);

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
