using HWSuite.Core.Network;
using HWSuite.Gui.Hud;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>
/// The dashboard's network panel, the same in every theme: connection, latency/jitter/loss to the router and to
/// the Internet over the last minute (with a chart), throughput, and a gaming verdict with a hint of where a
/// problem is.
/// </summary>
internal sealed class DashboardNetwork
{
    private readonly TextBlock _conn = DashboardViews.Text(size: 15, bold: true);
    private readonly TextBlock _connSub = DashboardViews.Text(size: 12.5, brush: HudUi.TextDim);
    private readonly TextBlock _rating = DashboardViews.Text(size: 15, bold: true, font: "HudDisplayFont");
    private readonly TextBlock _hint = DashboardViews.Text(size: 12.5, brush: HudUi.TextDim);
    private readonly Border _dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
    private readonly (TextBlock Title, TextBlock Latency, TextBlock Jitter, TextBlock Loss, TextBlock Range) _router;
    private readonly (TextBlock Title, TextBlock Latency, TextBlock Jitter, TextBlock Loss, TextBlock Range) _internet;
    private readonly LineChart _pingChart = new() { Height = 64, Unit = " ms", GridLines = 2, Capacity = LiveNetworkMonitor.Window };
    private readonly TextBlock _down = DashboardViews.Text(size: 26, font: "HudDisplayFont", bold: true);
    private readonly TextBlock _up = DashboardViews.Text(size: 26, font: "HudDisplayFont", bold: true);
    private readonly LineChart _rateChart = new() { Height = 48, Unit = "", GridLines = 1, Capacity = LiveNetworkMonitor.Window };

    public DashboardNetwork()
    {
        var s = App.Text;
        var grid = new Grid { ColumnSpacing = 22, RowSpacing = 12 };
        Responsive.SetMinColumn(grid, 230);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

        // Column 0: connection and verdict.
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(_conn);
        left.Children.Add(_connSub);
        var verdict = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        verdict.Children.Add(_dot);
        verdict.Children.Add(_rating);
        left.Children.Add(verdict);
        _hint.TextWrapping = TextWrapping.WrapWholeWords;
        _hint.TextTrimming = TextTrimming.None;
        left.Children.Add(_hint);
        grid.Children.Add(left);

        // Columns 1 and 2: router and Internet.
        _router = Block(s["dash.net.router"]);
        _internet = Block("");
        grid.Children.Add(DashboardViews.Col(BlockPanel(_router), 1));
        grid.Children.Add(DashboardViews.Col(BlockPanel(_internet), 2));

        // Column 3: throughput.
        var right = new StackPanel { Spacing = 4 };
        var rates = DashboardViews.Columns(1, 1);
        rates.Children.Add(Rate(s["dash.net.down"], _down, HudUi.Accent));
        rates.Children.Add(DashboardViews.Col(Rate(s["dash.net.up"], _up, HudUi.Warn), 1));
        right.Children.Add(rates);
        right.Children.Add(_rateChart);
        grid.Children.Add(DashboardViews.Col(right, 3));

        // Row 1: latency chart over the full width.
        var chartHead = DashboardViews.Text($"{s["dash.net.latency"]} · {s["dash.net.window"]}", 11.5, HudUi.Tag);
        var chart = new StackPanel { Spacing = 4 };
        chart.Children.Add(chartHead);
        chart.Children.Add(_pingChart);
        Grid.SetRow(chart, 1);
        Grid.SetColumnSpan(chart, 4);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(chart);

        Root = DashboardViews.Panel(s["dash.net"], grid);
    }

    public FrameworkElement Root { get; }

    private static (TextBlock, TextBlock, TextBlock, TextBlock, TextBlock) Block(string title)
    {
        var big = DashboardViews.Text(size: 30, font: "HudDisplayFont", bold: true);
        return (DashboardViews.Text(title, 12, HudUi.Tag, "HudMonoFont"), big, Value(), Value(), Value());
    }

    private static TextBlock Value() => DashboardViews.Text(size: 13.5, font: "HudMonoFont");

    private static StackPanel BlockPanel((TextBlock Title, TextBlock Latency, TextBlock Jitter, TextBlock Loss, TextBlock Range) b)
    {
        var s = App.Text;
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(b.Title);
        stack.Children.Add(b.Latency);
        stack.Children.Add(Row(s["dash.net.jitter"], b.Jitter));
        stack.Children.Add(Row(s["dash.net.loss"], b.Loss));
        stack.Children.Add(Row(s["dash.net.range"], b.Range));
        return stack;
    }

    private static Grid Row(string label, TextBlock value)
    {
        var row = DashboardViews.Columns(1, 0);
        row.ColumnDefinitions[1].Width = GridLength.Auto;
        row.Children.Add(DashboardViews.Text(label, 12.5, HudUi.TextDim));
        row.Children.Add(DashboardViews.Col(value, 1));
        return row;
    }

    private static StackPanel Rate(string label, TextBlock value, Brush color)
    {
        value.Foreground = color;
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(DashboardViews.Text(label, 12, HudUi.Tag, "HudMonoFont"));
        stack.Children.Add(value);
        stack.Children.Add(DashboardViews.Text("Mbps", 11.5, HudUi.TextDim));
        return stack;
    }

    public void Update(LiveNetworkReading r)
    {
        var s = App.Text;
        var c = DashboardViews.C;
        string Ms(double? v) => v is { } x ? x.ToString(x < 10 ? "0.0" : "0", c) + " ms" : "—";

        if (r.Connection is { } conn)
        {
            _conn.Text = conn.IsWifi ? $"Wi-Fi · {conn.Wifi?.Ssid ?? conn.Name}" : $"{(conn.IsEthernet ? "Ethernet" : conn.Name)} · {conn.Description}";
            var parts = new List<string>();
            if (conn.LinkMbps is { } link)
            {
                parts.Add($"{s["dash.net.link"]} {link.ToString("N0", c)} Mbps");
            }

            if (conn.Wifi is { } w)
            {
                parts.Add($"{s["dash.net.signal"]} {w.SignalPercent}%{(w.RssiDbm is { } dbm ? $" ({dbm} dBm)" : "")}");
                if (w.BandGhz is { } band)
                {
                    parts.Add($"{band.ToString("0.#", c)} GHz{(w.Channel is { } ch ? $" · ch {ch}" : "")}");
                }

                if (w.Standard is { } std)
                {
                    parts.Add(std);
                }
            }

            if (r.Gateway is { } gw)
            {
                parts.Add(gw);
            }

            _connSub.Text = string.Join(" · ", parts);
        }
        else
        {
            _conn.Text = s["dash.net.none"];
            _connSub.Text = "";
        }

        _internet.Title.Text = s.Format("dash.net.internet", r.Target);
        Show(_router, r.Router, Ms);
        Show(_internet, r.Internet, Ms);

        var rating = r.Rating;
        var brush = rating switch
        {
            GameRating.Excellent or GameRating.Good => HudUi.Good,
            GameRating.Fair => HudUi.Warn,
            GameRating.Poor => HudUi.Crit,
            _ => HudUi.TextDim,
        };
        _rating.Text = s[$"dash.net.rating.{rating}"];
        _rating.Foreground = brush;
        _dot.Background = brush;
        var routerBad = r.Router.Sent >= 5 && (r.Router.LossPercent > 0 || r.Router.JitterMs >= 10);
        _hint.Text = rating is GameRating.Fair or GameRating.Poor
            ? routerBad ? s["dash.net.hint.router"] : r.Router.Sent >= 5 ? s["dash.net.hint.isp"] : ""
            : r.Connection?.IsWifi == true ? s["dash.net.hint.wifi"] : "";

        _pingChart.Set(
        [
            new ChartSeries(r.Internet.History, HudUi.Accent, Fill: true),
            new ChartSeries(r.Router.History, HudUi.Warn),
        ]);

        string Mbps(double? v) => v is { } x ? x.ToString(x < 10 ? "0.00" : x < 100 ? "0.0" : "0", c) : "—";
        _down.Text = Mbps(r.DownMbps);
        _up.Text = Mbps(r.UpMbps);
        _rateChart.Set(
        [
            new ChartSeries(r.DownHistory, HudUi.Accent, Fill: true),
            new ChartSeries(r.UpHistory, HudUi.Warn),
        ]);
    }

    private static void Show((TextBlock Title, TextBlock Latency, TextBlock Jitter, TextBlock Loss, TextBlock Range) b, PingWindow w, Func<double?, string> ms)
    {
        var c = DashboardViews.C;
        b.Latency.Text = w.Sent == 0 ? "—" : w.LastMs is null ? "timeout" : ms(w.LastMs);
        b.Latency.Foreground = w.LastMs is null && w.Sent > 0 ? HudUi.Crit : w.LastMs >= 100 ? HudUi.Crit : w.LastMs >= 60 ? HudUi.Warn : HudUi.Text;
        b.Jitter.Text = ms(w.JitterMs);
        b.Jitter.Foreground = w.JitterMs >= 30 ? HudUi.Crit : w.JitterMs >= 15 ? HudUi.Warn : HudUi.Text;
        b.Loss.Text = w.Sent == 0 ? "—" : $"{w.LossPercent.ToString("0.#", c)}%  ({w.Sent - w.History.Count}/{w.Sent})";
        b.Loss.Foreground = w.LossPercent >= 2 ? HudUi.Crit : w.LossPercent > 0 ? HudUi.Warn : HudUi.Good;
        b.Range.Text = w.MinMs is null ? "—" : $"{ms(w.MinMs)} / {ms(w.MaxMs)}";
    }

    /// <summary>A believable reading for demo mode and the CI screenshots.</summary>
    public static LiveNetworkReading Demo()
    {
        var rnd = new Random(7);
        double?[] router = [.. Enumerable.Range(0, 60).Select(_ => (double?)(1 + (rnd.NextDouble() * 1.5)))];
        double?[] internet = [.. Enumerable.Range(0, 60).Select(i => i == 37 ? null : (double?)(11 + (rnd.NextDouble() * 6) + (i is > 20 and < 26 ? 9 : 0)))];
        double[] down = [.. Enumerable.Range(0, 60).Select(i => 2 + (rnd.NextDouble() * 4) + (i is > 30 and < 45 ? 180 : 0))];
        double[] up = [.. Enumerable.Range(0, 60).Select(_ => 0.4 + rnd.NextDouble())];
        return new LiveNetworkReading(
            new NetConnection("Ethernet", "Realtek Gaming 2.5GbE", false, true, 2500, null),
            "192.168.0.1",
            "1.1.1.1",
            PingWindow.From(router),
            PingWindow.From(internet),
            down[^1],
            up[^1],
            down,
            up);
    }
}
