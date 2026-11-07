using System.Globalization;
using HWSuite.Core.Sensors;
using HWSuite.Gui.Hud;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HWSuite.Gui.Pages;

/// <summary>A theme's own way of showing the dashboard (the HUD theme keeps the page's original layout).</summary>
internal interface IDashboardView
{
    UIElement Root { get; }

    void Update(DashboardReadout d, SensorSnapshot snapshot);
}

/// <summary>Picks the dashboard view of a theme; null = the original HUD layout.</summary>
internal static class DashboardViews
{
    public static IDashboardView? For(string themeId) => themeId switch
    {
        "fluent" or "minimal" => new CardsView(themeId == "minimal"),
        "compact" => new TableView(),
        "metal" => new DialsView(),
        "terminal" => new TerminalView(),
        "cockpit" => new CockpitView(),
        "hologram" => new HologramView(),
        _ => null,
    };

    // ---- Shared pieces ----------------------------------------------------------------------------------------

    public static CultureInfo C => App.Text.Culture;

    public static string N(double? v, string format = "0") => v is { } x ? x.ToString(format, C) : "—";

    public static TextBlock Text(string text = "", double size = 14, Brush? brush = null, string? font = null, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? HudUi.Text,
        FontFamily = font is null ? HudUi.Font("HudBodyFont") : HudUi.Font(font),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static Border Bar(double fraction, Brush fill, double height = 6, double radius = 3)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(radius), Background = HudUi.Brush("HudAccentFaintBrush") };
        var lit = new Border { Height = height, CornerRadius = new CornerRadius(radius), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = lit;
        track.SizeChanged += (_, e) => lit.Width = Math.Clamp(fraction, 0, 1) * e.NewSize.Width;
        track.Tag = lit;
        return track;
    }

    public static void SetBar(Border bar, double fraction, Brush? fill = null)
    {
        if (bar.Tag is Border lit)
        {
            lit.Width = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1) * bar.ActualWidth;
            if (fill is not null)
            {
                lit.Background = fill;
            }

            bar.SizeChanged -= Resize;
            bar.SizeChanged += Resize;
            bar.Resources["f"] = fraction;
        }
    }

    private static void Resize(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border { Tag: Border lit } bar && bar.Resources.TryGetValue("f", out var f) && f is double fraction)
        {
            lit.Width = Math.Clamp(fraction, 0, 1) * e.NewSize.Width;
        }
    }

    public static HudPanel Panel(string? title, UIElement content) => HudUi.Panel(title, content, C);

    public static Grid Columns(params double[] stars)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        foreach (var s in stars)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s, GridUnitType.Star) });
        }

        return grid;
    }

    public static T Col<T>(T element, int column)
        where T : FrameworkElement
    {
        Grid.SetColumn(element, column);
        return element;
    }

    /// <summary>Temperature history chart of CPU and GPU (accent and warn).</summary>
    public static LineChart TempChart(double height)
    {
        var chart = new LineChart { Height = height, Unit = "°" };
        return chart;
    }

    public static void ShowTemps(LineChart chart, DashboardReadout d)
    {
        var series = new List<ChartSeries>();
        if (d.Cpu.TemperatureId is { } cpu)
        {
            series.Add(new ChartSeries(App.Monitor.History.Get(cpu), HudUi.Accent, Fill: true));
        }

        if (d.Gpu.TemperatureId is { } gpu)
        {
            series.Add(new ChartSeries(App.Monitor.History.Get(gpu), HudUi.Warn));
        }

        chart.Capacity = App.Monitor.History.Capacity;
        chart.Set(series);
    }

    /// <summary>Fans and rails as label/value rows.</summary>
    public static IEnumerable<(string Label, string Value)> Cooling(DashboardReadout d)
    {
        foreach (var fan in d.Fans.Take(5))
        {
            yield return (fan.Name, $"{fan.Rpm.ToString("N0", C)} RPM");
        }

        foreach (var (rail, volts) in d.Rails)
        {
            yield return (rail.Name, $"{volts.ToString("0.00", C)} V");
        }
    }

    public static double? MemPercent(DashboardReadout d) => d.Memory.Load;

    public static double? VramPercent(DashboardReadout d) =>
        d.Gpu.MemoryUsedMb is { } u && d.Gpu.MemoryTotalMb is > 0 and var t ? u / t * 100 : null;

    /// <summary>The id of the first reading that matches (hardware kind, sensor kind, name contains).</summary>
    public static string? Find(SensorSnapshot s, string hardwareKind, SensorKind kind, params string[] names)
    {
        foreach (var name in names)
        {
            var r = s.Readings.FirstOrDefault(x => x.HardwareKind == hardwareKind && x.Kind == kind && x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                && !x.Hardware.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
            if (r is not null)
            {
                return r.Id;
            }
        }

        return s.Readings.FirstOrDefault(x => x.HardwareKind == hardwareKind && x.Kind == kind)?.Id;
    }
}

/// <summary>An arc gauge: a track and a lit part over <see cref="Sweep"/> degrees, open at the bottom, with content in the middle.</summary>
internal sealed partial class ArcDial : Grid
{
    private readonly Path _track = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _lit = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private double _value;

    public ArcDial(double size, double thickness, double sweep, UIElement center)
    {
        Width = size;
        Height = size;
        Thickness = thickness;
        Sweep = Math.Min(sweep, 359.5);
        _track.StrokeThickness = thickness;
        _lit.StrokeThickness = thickness;
        _track.Stroke = HudUi.Brush("HudAccentDimBrush");
        _lit.Stroke = HudUi.Accent;
        Children.Add(_track);
        Children.Add(_lit);
        Children.Add(center);
        Draw();
    }

    public double Thickness { get; }

    public double Sweep { get; }

    public Brush Fill
    {
        get => _lit.Stroke;
        set => _lit.Stroke = value;
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);
            Draw();
        }
    }

    private void Draw()
    {
        var r = (Width - Thickness) / 2;
        var start = 90 + ((360 - Sweep) / 2);
        _track.Data = Arc(r, start, Sweep);
        _lit.Data = _value > 0.002 ? Arc(r, start, Sweep * _value) : null;
    }

    private PathGeometry Arc(double r, double startDeg, double sweepDeg)
    {
        var cx = Width / 2;
        var cy = Height / 2;
        Point P(double deg) => new(cx + (r * Math.Cos(deg * Math.PI / 180)), cy + (r * Math.Sin(deg * Math.PI / 180)));
        var figure = new PathFigure { StartPoint = P(startDeg), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment { Point = P(startDeg + sweepDeg), Size = new Size(r, r), IsLargeArc = sweepDeg > 180, SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

// ---- Windows 11 / Minimal: big cards ---------------------------------------------------------------------------

internal sealed class CardsView : IDashboardView
{
    private readonly (TextBlock Label, TextBlock Value, TextBlock Unit, Border Bar, TextBlock Sub)[] _cards = new (TextBlock, TextBlock, TextBlock, Border, TextBlock)[4];
    private readonly LineChart _chart;
    private readonly StackPanel _cooling = new();
    private readonly TextBlock? _headline;

    public CardsView(bool minimal)
    {
        var s = App.Text;
        var root = new StackPanel { Spacing = 16 };
        if (minimal)
        {
            _headline = DashboardViews.Text(size: 26, bold: true, font: "HudDisplayFont");
            root.Children.Add(_headline);
        }

        var cards = new Grid { ColumnSpacing = 16 };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = DashboardViews.Text(size: 13, brush: HudUi.TextDim);
            var value = DashboardViews.Text(size: minimal ? 40 : 32, bold: true, font: "HudDisplayFont");
            var unit = DashboardViews.Text(size: 15, brush: HudUi.TextDim);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(0, 0, 0, 6);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(value);
            line.Children.Add(unit);
            var bar = DashboardViews.Bar(0, HudUi.Accent, 6, 3);
            var sub = DashboardViews.Text(size: 12.5, brush: HudUi.TextDim);
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(label);
            stack.Children.Add(line);
            stack.Children.Add(bar);
            stack.Children.Add(sub);
            cards.Children.Add(DashboardViews.Col(DashboardViews.Panel(null, stack), i));
            _cards[i] = (label, value, unit, bar, sub);
        }

        root.Children.Add(cards);
        var lower = DashboardViews.Columns(2, 1);
        _chart = DashboardViews.TempChart(220);
        lower.Children.Add(DashboardViews.Col(DashboardViews.Panel(s["dash.temps"].Replace("{0}", "5 min", StringComparison.Ordinal), _chart), 0));
        lower.Children.Add(DashboardViews.Col(DashboardViews.Panel(s["dash.cooling"], _cooling), 1));
        root.Children.Add(lower);
        Root = root;
    }

    public UIElement Root { get; }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var s = App.Text;
        var c = DashboardViews.C;
        void Card(int i, string label, string value, string unit, double? pct, string sub, Brush fill)
        {
            var card = _cards[i];
            card.Label.Text = label;
            card.Value.Text = value;
            card.Unit.Text = unit;
            DashboardViews.SetBar(card.Bar, (pct ?? 0) / 100, fill);
            card.Sub.Text = sub;
        }

        Card(0, $"{s["dash.cpu"]} · {d.Cpu.Name}", DashboardViews.N(d.Cpu.Temperature), "°C", d.Cpu.Load,
            $"{s.Format("dash.loadValue", DashboardViews.N(d.Cpu.Load))} · {DashboardViews.N(d.Cpu.ClockMhz / 1000, "0.00")} GHz · {DashboardViews.N(d.Cpu.PowerW, "0")} W", HudUi.Accent);
        Card(1, $"{s["dash.gpu"]} · {d.Gpu.Name}", DashboardViews.N(d.Gpu.Temperature), "°C", d.Gpu.Load,
            $"{s.Format("dash.loadValue", DashboardViews.N(d.Gpu.Load))} · {DashboardViews.N(d.Gpu.ClockMhz, "N0")} MHz · {DashboardViews.N(d.Gpu.PowerW, "0")} W", HudUi.Warn);
        Card(2, "RAM", DashboardViews.N(d.Memory.UsedGb, "0.0"), $"/ {DashboardViews.N(d.Memory.TotalGb, "0.0")} GB", d.Memory.Load,
            $"{DashboardViews.N(d.Memory.Load)}%", d.Memory.Load >= 90 ? HudUi.Warn : HudUi.Accent);
        Card(3, "VRAM", DashboardViews.N(d.Gpu.MemoryUsedMb / 1024, "0.0"), $"/ {DashboardViews.N(d.Gpu.MemoryTotalMb / 1024, "0")} GB", DashboardViews.VramPercent(d),
            $"{DashboardViews.N(DashboardViews.VramPercent(d))}%", HudUi.Warn);
        if (_headline is not null)
        {
            _headline.Text = s["dash.nominal"];
        }

        DashboardViews.ShowTemps(_chart, d);
        _cooling.Children.Clear();
        foreach (var (label, value) in DashboardViews.Cooling(d))
        {
            var row = DashboardViews.Columns(1, 0);
            row.ColumnDefinitions[1].Width = GridLength.Auto;
            row.Padding = new Thickness(0, 8, 0, 8);
            row.BorderBrush = HudUi.Brush("HudAccentFaintBrush");
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.Children.Add(DashboardViews.Text(label, 13.5));
            row.Children.Add(DashboardViews.Col(DashboardViews.Text(value, 13.5, HudUi.TextDim), 1));
            _cooling.Children.Add(row);
        }

        _ = c;
    }
}

// ---- Technical compact: everything in tables -------------------------------------------------------------------

internal sealed class TableView : IDashboardView
{
    private static readonly (string Kind, string Title)[] Groups =
    [
        ("CPU", "dash.cpu"), ("GPU", "dash.gpu"), ("Memory", "dash.memory"), ("Motherboard", "Motherboard"), ("Storage", "dash.storage"),
    ];

    private static readonly SensorKind[] Kinds = [SensorKind.Temperature, SensorKind.Load, SensorKind.Clock, SensorKind.Power, SensorKind.Voltage, SensorKind.Fan, SensorKind.Data, SensorKind.SmallData];

    private readonly MasonryPanel _panels = new() { MinColumnWidth = 380, Spacing = 8 };
    private readonly Dictionary<string, (TextBlock Value, TextBlock Min, TextBlock Max)> _rows = [];
    private string _layoutKey = "";

    public TableView() => Root = _panels;

    public UIElement Root { get; }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var c = DashboardViews.C;
        var groups = Groups
            .Select(g => (g.Title, Readings: snapshot.Readings
                .Where(r => (r.HardwareKind == g.Kind || (g.Kind == "Motherboard" && r.Parent is not null)) && Kinds.Contains(r.Kind)
                    && !r.Hardware.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => Array.IndexOf(Kinds, r.Kind))
                .Take(14)
                .ToList()))
            .Where(g => g.Readings.Count > 0)
            .ToList();
        var key = string.Join("|", groups.SelectMany(g => g.Readings.Select(r => r.Id)));
        if (key != _layoutKey)
        {
            _layoutKey = key;
            _rows.Clear();
            _panels.Children.Clear();
            var s = App.Text;
            foreach (var (title, readings) in groups)
            {
                var list = new StackPanel();
                list.Children.Add(Row(s["sensors.colChannel"], s["sensors.colNow"], s["sensors.min"], s["sensors.max"], header: true).Grid);
                foreach (var r in readings)
                {
                    var row = Row(r.Name, "", "", "");
                    _rows[r.Id] = (row.Value, row.Min, row.Max);
                    list.Children.Add(row.Grid);
                }

                var name = readings[0].Hardware;
                _panels.Children.Add(DashboardViews.Panel($"{(title.Contains('.', StringComparison.Ordinal) ? s[title] : title)} · {name}", list));
            }
        }

        foreach (var r in snapshot.Readings)
        {
            if (_rows.TryGetValue(r.Id, out var row))
            {
                row.Value.Text = SensorFormat.Format(r.Value, r.Kind, c);
                row.Min.Text = SensorFormat.Format(r.Min, r.Kind, c);
                row.Max.Text = SensorFormat.Format(r.Max, r.Kind, c);
                row.Value.Foreground = r.Kind == SensorKind.Temperature && r.Value >= 85 ? HudUi.Crit : r.Kind == SensorKind.Temperature && r.Value >= 75 ? HudUi.Warn : HudUi.Text;
            }
        }
    }

    private static (Grid Grid, TextBlock Value, TextBlock Min, TextBlock Max) Row(string name, string value, string min, string max, bool header = false)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 3, 0, 3), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var size = header ? 11 : 12;
        var dim = header ? HudUi.TextDim : null;
        var n = DashboardViews.Text(name, size, dim);
        var v = DashboardViews.Text(value, size, dim, header ? null : "HudMonoFont", bold: !header);
        var lo = DashboardViews.Text(min, size, HudUi.TextDim, header ? null : "HudMonoFont");
        var hi = DashboardViews.Text(max, size, HudUi.TextDim, header ? null : "HudMonoFont");
        foreach (var t in new[] { v, lo, hi })
        {
            t.TextAlignment = TextAlignment.Right;
        }

        grid.Children.Add(n);
        grid.Children.Add(DashboardViews.Col(v, 1));
        grid.Children.Add(DashboardViews.Col(lo, 2));
        grid.Children.Add(DashboardViews.Col(hi, 3));
        return (grid, v, lo, hi);
    }
}

// ---- Metal: four round dials -----------------------------------------------------------------------------------

internal sealed class DialsView : IDashboardView
{
    private readonly (ArcDial Gauge, TextBlock Value, TextBlock Unit, TextBlock Sub)[] _dials = new (ArcDial, TextBlock, TextBlock, TextBlock)[4];
    private readonly LineChart _chart = DashboardViews.TempChart(200);
    private readonly StackPanel _cooling = new();

    public DialsView()
    {
        var s = App.Text;
        var root = new StackPanel { Spacing = 16 };
        var row = new Grid { ColumnSpacing = 16 };
        string[] titles = ["CPU", "GPU", "RAM", "VRAM"];
        for (var i = 0; i < 4; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var value = DashboardViews.Text(size: 30, bold: true, font: "HudDisplayFont");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var unit = DashboardViews.Text(size: 12, brush: HudUi.TextDim);
            unit.HorizontalAlignment = HorizontalAlignment.Center;
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            center.Children.Add(value);
            center.Children.Add(unit);
            var gauge = new ArcDial(150, 14, 300, center) { HorizontalAlignment = HorizontalAlignment.Center };
            var sub = DashboardViews.Text(size: 12.5, brush: HudUi.TextDim);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            var tag = DashboardViews.Text(titles[i], 12, HudUi.Brush("HudTagBrush"), "HudDisplayFont", bold: true);
            tag.HorizontalAlignment = HorizontalAlignment.Center;
            tag.CharacterSpacing = 200;
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(tag);
            stack.Children.Add(gauge);
            stack.Children.Add(sub);
            row.Children.Add(DashboardViews.Col(DashboardViews.Panel(null, stack), i));
            _dials[i] = (gauge, value, unit, sub);
        }

        root.Children.Add(row);
        var lower = DashboardViews.Columns(2, 1);
        lower.Children.Add(DashboardViews.Col(DashboardViews.Panel(s["dash.temps"].Replace("{0}", "5 min", StringComparison.Ordinal), _chart), 0));
        lower.Children.Add(DashboardViews.Col(DashboardViews.Panel(s["dash.cooling"], _cooling), 1));
        root.Children.Add(lower);
        Root = root;
    }

    public UIElement Root { get; }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var s = App.Text;
        void Dial(int i, double? fraction, string value, string unit, string sub)
        {
            _dials[i].Gauge.Value = fraction ?? 0;
            _dials[i].Gauge.Fill = fraction >= 0.9 ? HudUi.Crit : fraction >= 0.8 ? HudUi.Warn : HudUi.Accent;
            _dials[i].Value.Text = value;
            _dials[i].Unit.Text = unit;
            _dials[i].Sub.Text = sub;
        }

        Dial(0, d.Cpu.Temperature / 100, $"{DashboardViews.N(d.Cpu.Temperature)}°", MemoryReadout.ShortGpuName(d.Cpu.Name ?? "").Replace("Ryzen ", "", StringComparison.Ordinal), s.Format("dash.loadValue", DashboardViews.N(d.Cpu.Load)) + $" · {DashboardViews.N(d.Cpu.ClockMhz / 1000, "0.00")} GHz");
        Dial(1, d.Gpu.Temperature / 100, $"{DashboardViews.N(d.Gpu.Temperature)}°", MemoryReadout.ShortGpuName(d.Gpu.Name ?? ""), s.Format("dash.loadValue", DashboardViews.N(d.Gpu.Load)) + $" · {DashboardViews.N(d.Gpu.ClockMhz, "N0")} MHz");
        Dial(2, d.Memory.Load / 100, $"{DashboardViews.N(d.Memory.Load)}%", $"{DashboardViews.N(d.Memory.UsedGb, "0.0")} / {DashboardViews.N(d.Memory.TotalGb, "0.0")} GB", "RAM");
        Dial(3, DashboardViews.VramPercent(d) / 100, $"{DashboardViews.N(DashboardViews.VramPercent(d))}%", $"{DashboardViews.N(d.Gpu.MemoryUsedMb / 1024, "0.0")} / {DashboardViews.N(d.Gpu.MemoryTotalMb / 1024, "0")} GB", "VRAM");
        DashboardViews.ShowTemps(_chart, d);
        _cooling.Children.Clear();
        foreach (var (label, value) in DashboardViews.Cooling(d))
        {
            var row = DashboardViews.Columns(1, 0);
            row.ColumnDefinitions[1].Width = GridLength.Auto;
            row.Padding = new Thickness(0, 7, 0, 7);
            row.Children.Add(DashboardViews.Text(label, 13.5));
            row.Children.Add(DashboardViews.Col(DashboardViews.Text(value, 13.5, HudUi.Accent, bold: true), 1));
            _cooling.Children.Add(row);
        }
    }
}

// ---- Retro terminal: text blocks with character bars ------------------------------------------------------------

internal sealed class TerminalView : IDashboardView
{
    private readonly TextBlock _cpu = Block();
    private readonly TextBlock _gpu = Block();
    private readonly TextBlock _mem = Block();
    private readonly TextBlock _cool = Block();
    private readonly LineChart _chart = DashboardViews.TempChart(150);

    public TerminalView()
    {
        var s = App.Text;
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(grid, Box(s["dash.cpu"], _cpu), 0, 0);
        Add(grid, Box(s["dash.gpu"], _gpu), 0, 1);
        Add(grid, Box(s["dash.memory"], _mem), 1, 0);
        Add(grid, Box(s["dash.cooling"], _cool), 1, 1);
        var chart = Box(s["dash.temps"].Replace("{0}", "5 min", StringComparison.Ordinal), _chart);
        Grid.SetColumnSpan(chart, 2);
        Add(grid, chart, 2, 0);
        Root = grid;
    }

    public UIElement Root { get; }

    private static TextBlock Block() => new()
    {
        FontFamily = HudUi.Font("HudMonoFont"),
        FontSize = 14,
        Foreground = HudUi.Text,
        LineHeight = 22,
    };

    private static void Add(Grid grid, FrameworkElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        grid.Children.Add(e);
    }

    private static Border Box(string title, UIElement content)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = $"┤ {title.ToUpper(DashboardViews.C)} ├", FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 13, Foreground = HudUi.Accent });
        stack.Children.Add(content);
        return new Border { BorderBrush = HudUi.Accent, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 12), Child = stack };
    }

    private static string Bar(double? percent, int width = 22)
    {
        var n = (int)Math.Round(Math.Clamp(percent ?? 0, 0, 100) * width / 100);
        return "[" + new string('█', n) + new string('░', width - n) + "]";
    }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var s = App.Text;
        string N(double? v, string f = "0") => DashboardViews.N(v, f);
        const string Blocks = " ▁▂▃▄▅▆▇█";
        var threads = new string([.. d.Cpu.ThreadLoads.Select(v => Blocks[(int)Math.Clamp(Math.Round(v / 12.5), 0, 8)])]);
        _cpu.Text = $"{d.Cpu.Name}\nTEMP    {N(d.Cpu.Temperature, "0.0"),6} °C  {Bar(d.Cpu.Temperature)}\nCARGA   {N(d.Cpu.Load),6} %   {Bar(d.Cpu.Load)}\nCLOCK   {N(d.Cpu.ClockMhz / 1000, "0.00"),6} GHz\nPOT.    {N(d.Cpu.PowerW, "0.0"),6} W\nVCORE   {N(d.Cpu.CoreVolts, "0.000"),6} V\nTHREADS {threads}";
        _gpu.Text = $"{d.Gpu.Name}\nTEMP    {N(d.Gpu.Temperature, "0.0"),6} °C  {Bar(d.Gpu.Temperature)}\nHOTSPOT {N(d.Gpu.HotSpot, "0.0"),6} °C  {Bar(d.Gpu.HotSpot)}\nCARGA   {N(d.Gpu.Load),6} %   {Bar(d.Gpu.Load)}\nCLOCK   {N(d.Gpu.ClockMhz, "0"),6} MHz\nPOT.    {N(d.Gpu.PowerW),6} W";
        _mem.Text = $"RAM   {N(d.Memory.UsedGb, "0.0")}/{N(d.Memory.TotalGb, "0.0")} GB  {Bar(d.Memory.Load)}\nVRAM  {N(d.Gpu.MemoryUsedMb / 1024, "0.0")}/{N(d.Gpu.MemoryTotalMb / 1024, "0.0")} GB  {Bar(DashboardViews.VramPercent(d))}";
        _cool.Text = string.Join("\n", DashboardViews.Cooling(d).Select(x => $"{(x.Label.Length > 18 ? x.Label[..18] : x.Label),-18} {x.Value,12}"));
        DashboardViews.ShowTemps(_chart, d);
        _ = s;
    }
}

// ---- Cockpit: tachometers and LED strips -----------------------------------------------------------------------

internal sealed class CockpitView : IDashboardView
{
    private readonly (ArcDial Gauge, TextBlock Value, TextBlock Sub)[] _gauges = new (ArcDial, TextBlock, TextBlock)[3];
    private readonly (TextBlock Value, StackPanel Leds)[] _strip = new (TextBlock, StackPanel)[6];
    private readonly TextBlock[] _stripLabels = new TextBlock[6];
    private readonly LineChart _chart = DashboardViews.TempChart(150);

    public CockpitView()
    {
        var s = App.Text;
        var root = new StackPanel { Spacing = 18 };
        var gauges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 34, HorizontalAlignment = HorizontalAlignment.Center };
        (string Label, double Size, double Font)[] spec = [("GPU", 210, 52), ("CPU", 290, 80), ("RAM", 210, 52)];
        for (var i = 0; i < 3; i++)
        {
            var label = DashboardViews.Text(spec[i].Label, 14, HudUi.TextDim, "HudDisplayFont");
            label.CharacterSpacing = 200;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var value = DashboardViews.Text(size: spec[i].Font, bold: true, font: "HudDisplayFont");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var sub = DashboardViews.Text(size: 14, brush: HudUi.Accent, font: "HudDisplayFont");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            center.Children.Add(label);
            center.Children.Add(value);
            center.Children.Add(sub);
            var gauge = new ArcDial(spec[i].Size, i == 1 ? 18 : 14, 270, center) { VerticalAlignment = VerticalAlignment.Center };
            gauges.Children.Add(gauge);
            _gauges[i] = (gauge, value, sub);
        }

        root.Children.Add(gauges);
        var strip = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < 6; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            var label = DashboardViews.Text(size: 13, brush: HudUi.TextDim, font: "HudDisplayFont");
            label.CharacterSpacing = 100;
            var value = DashboardViews.Text(size: 24, bold: true, font: "HudDisplayFont");
            var leds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (var l = 0; l < 12; l++)
            {
                leds.Children.Add(new Rectangle { Width = 9, Height = 6, RadiusX = 1, RadiusY = 1 });
            }

            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(label);
            stack.Children.Add(value);
            stack.Children.Add(leds);
            strip.Children.Add(DashboardViews.Col(DashboardViews.Panel(null, stack), i));
            _strip[i] = (value, leds);
            _stripLabels[i] = label;
        }

        root.Children.Add(strip);
        root.Children.Add(DashboardViews.Panel(s["dash.temps"].Replace("{0}", "5 min", StringComparison.Ordinal), _chart));
        Root = root;
    }

    public UIElement Root { get; }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var s = App.Text;
        void Gauge(int i, double? fraction, string value, string sub)
        {
            _gauges[i].Gauge.Value = fraction ?? 0;
            _gauges[i].Gauge.Fill = fraction >= 0.9 ? HudUi.Crit : fraction >= 0.8 ? HudUi.Warn : HudUi.Accent;
            _gauges[i].Value.Text = value;
            _gauges[i].Sub.Text = sub;
        }

        Gauge(0, d.Gpu.Temperature / 100, $"{DashboardViews.N(d.Gpu.Temperature)}°", $"{DashboardViews.N(d.Gpu.ClockMhz, "N0")} MHz");
        Gauge(1, d.Cpu.Temperature / 100, $"{DashboardViews.N(d.Cpu.Temperature)}°", s.Format("dash.loadValue", DashboardViews.N(d.Cpu.Load)).ToUpper(DashboardViews.C) + $" · {DashboardViews.N(d.Cpu.ClockMhz / 1000, "0.00")} GHz");
        Gauge(2, d.Memory.Load / 100, $"{DashboardViews.N(d.Memory.Load)}%", $"{DashboardViews.N(d.Memory.UsedGb, "0.0")} / {DashboardViews.N(d.Memory.TotalGb, "0.0")} GB");

        var fan = d.Fans.FirstOrDefault();
        var twelve = d.Rails.FirstOrDefault(r => r.Rail.Nominal == 12);
        (string Label, string Value, double Fraction)[] items =
        [
            (s["dash.power"], $"{DashboardViews.N(d.Cpu.PowerW)} W", (d.Cpu.PowerW ?? 0) / 150),
            ("VCORE", $"{DashboardViews.N(d.Cpu.CoreVolts, "0.000")} V", ((d.Cpu.CoreVolts ?? 0) - 0.8) / 0.7),
            ("HOT SPOT", $"{DashboardViews.N(d.Gpu.HotSpot)} °C", (d.Gpu.HotSpot ?? 0) / 110),
            ("VRAM", $"{DashboardViews.N(d.Gpu.MemoryUsedMb / 1024, "0.0")} GB", (DashboardViews.VramPercent(d) ?? 0) / 100),
            (fan?.Name ?? "FAN", fan is null ? "—" : $"{fan.Rpm.ToString("N0", DashboardViews.C)} rpm", (fan?.Rpm ?? 0) / 3000),
            ("+12 V", twelve.Volts > 0 ? $"{twelve.Volts.ToString("0.00", DashboardViews.C)} V" : "—", twelve.Volts > 0 ? (twelve.Volts - 11) / 1.6 : 0),
        ];
        for (var i = 0; i < 6; i++)
        {
            _stripLabels[i].Text = items[i].Label.ToUpper(DashboardViews.C);
            _strip[i].Value.Text = items[i].Value;
            var lit = (int)Math.Round(Math.Clamp(items[i].Fraction, 0, 1) * 12);
            for (var l = 0; l < 12; l++)
            {
                ((Rectangle)_strip[i].Leds.Children[l]).Fill = l < lit ? (l >= 10 ? HudUi.Crit : l >= 8 ? HudUi.Warn : HudUi.Accent) : HudUi.Brush("HudAccentFaintBrush");
            }
        }

        DashboardViews.ShowTemps(_chart, d);
    }
}

// ---- Hologram: arcs and bars, a grid of live charts, rings and MAX/MIN bars --------------------------------------

internal sealed class HologramView : IDashboardView
{
    private readonly (ArcDial Gauge, TextBlock Value)[] _arcs = new (ArcDial, TextBlock)[2];
    private readonly Border[] _vbars = new Border[4];
    private readonly (TextBlock Label, TextBlock Value, LineChart Chart)[] _tiles = new (TextBlock, TextBlock, LineChart)[9];
    private readonly (ArcDial Gauge, TextBlock Value)[] _rings = new (ArcDial, TextBlock)[4];
    private readonly Border[] _tall = new Border[3];

    public HologramView()
    {
        var root = new Grid { ColumnSpacing = 14 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        // Left: two half-circle arcs, four vertical load bars.
        var left = new StackPanel { Spacing = 14 };
        string[] arcLabels = ["CPU · TEMP", "GPU · HOT SPOT"];
        for (var i = 0; i < 2; i++)
        {
            var value = DashboardViews.Text(size: 26, bold: true, font: "HudDisplayFont");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(0, 40, 0, 0);
            var gauge = new ArcDial(160, 12, 180, value) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -50) };
            var label = DashboardViews.Text(arcLabels[i], 12, HudUi.Brush("HudTagBrush"), "HudMonoFont");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel();
            stack.Children.Add(gauge);
            stack.Children.Add(label);
            left.Children.Add(stack);
            _arcs[i] = (gauge, value);
        }

        var bars = new Grid { ColumnSpacing = 10, Height = 200, Margin = new Thickness(0, 10, 0, 0) };
        string[] barLabels = ["CPU", "GPU", "RAM", "VRAM"];
        for (var i = 0; i < 4; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var track = new Border { BorderBrush = HudUi.Accent, BorderThickness = new Thickness(1), Padding = new Thickness(2), Width = 22, HorizontalAlignment = HorizontalAlignment.Center };
            var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = HudUi.Accent, Height = 0 };
            track.Child = fill;
            var label = DashboardViews.Text(barLabels[i], 10, HudUi.Brush("HudTagBrush"), "HudMonoFont");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var col = new Grid { RowSpacing = 4 };
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.Children.Add(track);
            Grid.SetRow(label, 1);
            col.Children.Add(label);
            bars.Children.Add(DashboardViews.Col(col, i));
            _vbars[i] = fill;
        }

        left.Children.Add(bars);
        root.Children.Add(DashboardViews.Col(DashboardViews.Panel(null, left), 0));

        // Center: 3 × 3 live charts.
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < 9; i++)
        {
            var label = DashboardViews.Text(size: 12, brush: HudUi.Brush("HudTagBrush"), font: "HudMonoFont");
            var value = DashboardViews.Text(size: 13, bold: true);
            var head = DashboardViews.Columns(1, 0);
            head.ColumnDefinitions[1].Width = GridLength.Auto;
            head.Children.Add(label);
            head.Children.Add(DashboardViews.Col(value, 1));
            var chart = new LineChart { Height = 96, GridLines = 2 };
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(head);
            stack.Children.Add(chart);
            var panel = DashboardViews.Panel(null, stack);
            panel.Padding = new Thickness(10, 8, 10, 8);
            Grid.SetRow(panel, i / 3);
            Grid.SetColumn(panel, i % 3);
            grid.Children.Add(panel);
            _tiles[i] = (label, value, chart);
        }

        root.Children.Add(DashboardViews.Col(grid, 1));

        // Right: four rings, then MAX/MIN bars.
        var right = new StackPanel { Spacing = 10 };
        string[] ringLabels = ["CPU", "GPU", "RAM", "VRAM"];
        var rings = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        rings.ColumnDefinitions.Add(new ColumnDefinition());
        rings.ColumnDefinitions.Add(new ColumnDefinition());
        rings.RowDefinitions.Add(new RowDefinition());
        rings.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < 4; i++)
        {
            var value = DashboardViews.Text(size: 16, bold: true, font: "HudDisplayFont");
            value.HorizontalAlignment = HorizontalAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Center;
            var gauge = new ArcDial(74, 7, 360, value) { HorizontalAlignment = HorizontalAlignment.Center };
            var label = DashboardViews.Text(ringLabels[i], 11, HudUi.Brush("HudTagBrush"), "HudMonoFont");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(gauge);
            stack.Children.Add(label);
            Grid.SetRow(stack, i / 2);
            Grid.SetColumn(stack, i % 2);
            rings.Children.Add(stack);
            _rings[i] = (gauge, value);
        }

        right.Children.Add(rings);
        var max = DashboardViews.Text("MAX", 13, bold: true, font: "HudDisplayFont");
        max.HorizontalAlignment = HorizontalAlignment.Center;
        right.Children.Add(max);
        var tall = new Grid { ColumnSpacing = 8, Height = 180 };
        for (var i = 0; i < 3; i++)
        {
            tall.ColumnDefinitions.Add(new ColumnDefinition());
            var track = new Border { BorderBrush = HudUi.Accent, BorderThickness = new Thickness(1), Padding = new Thickness(2) };
            var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = HudUi.Accent, Height = 0 };
            track.Child = fill;
            tall.Children.Add(DashboardViews.Col(track, i));
            _tall[i] = fill;
        }

        right.Children.Add(tall);
        var min = DashboardViews.Text("MIN", 13, bold: true, font: "HudDisplayFont");
        min.HorizontalAlignment = HorizontalAlignment.Center;
        right.Children.Add(min);
        var legend = DashboardViews.Text("CPU · GPU · RAM", 10, HudUi.Brush("HudTagBrush"), "HudMonoFont");
        legend.HorizontalAlignment = HorizontalAlignment.Center;
        right.Children.Add(legend);
        root.Children.Add(DashboardViews.Col(DashboardViews.Panel(null, right), 2));
        Root = root;
    }

    public UIElement Root { get; }

    private static void SetFill(Border fill, double? percent, double height)
    {
        fill.Height = Math.Clamp(percent ?? 0, 0, 100) / 100 * Math.Max(0, height - 6);
    }

    public void Update(DashboardReadout d, SensorSnapshot snapshot)
    {
        var s = App.Text;
        _arcs[0].Gauge.Value = (d.Cpu.Temperature ?? 0) / 100;
        _arcs[0].Value.Text = $"{DashboardViews.N(d.Cpu.Temperature)}°";
        _arcs[1].Gauge.Value = (d.Gpu.HotSpot ?? d.Gpu.Temperature ?? 0) / 110;
        _arcs[1].Value.Text = $"{DashboardViews.N(d.Gpu.HotSpot ?? d.Gpu.Temperature)}°";
        double?[] loads = [d.Cpu.Load, d.Gpu.Load, d.Memory.Load, DashboardViews.VramPercent(d)];
        for (var i = 0; i < 4; i++)
        {
            SetFill(_vbars[i], loads[i], 170);
            _rings[i].Gauge.Value = (loads[i] ?? 0) / 100;
            _rings[i].Value.Text = $"{DashboardViews.N(loads[i])}%";
        }

        for (var i = 0; i < 3; i++)
        {
            SetFill(_tall[i], loads[i], 180);
        }

        // The nine live charts: whatever of these sensors the machine has.
        var hist = App.Monitor.History;
        string? Id(string hw, SensorKind kind, params string[] names) => DashboardViews.Find(snapshot, hw, kind, names);
        (string Label, string? Id, string Value)[] tiles =
        [
            ("CPU · TEMP", d.Cpu.TemperatureId, $"{DashboardViews.N(d.Cpu.Temperature)} °C"),
            (s["dash.load"].ToUpper(DashboardViews.C) + " CPU", Id("CPU", SensorKind.Load, "CPU Total"), $"{DashboardViews.N(d.Cpu.Load)} %"),
            (s["dash.power"].ToUpper(DashboardViews.C) + " CPU", Id("CPU", SensorKind.Power, "Package"), $"{DashboardViews.N(d.Cpu.PowerW)} W"),
            ("GPU · TEMP", d.Gpu.TemperatureId, $"{DashboardViews.N(d.Gpu.Temperature)} °C"),
            (s["dash.load"].ToUpper(DashboardViews.C) + " GPU", Id("GPU", SensorKind.Load, "GPU Core", "D3D 3D"), $"{DashboardViews.N(d.Gpu.Load)} %"),
            ("GPU · CLOCK", Id("GPU", SensorKind.Clock, "GPU Core", "Core"), $"{DashboardViews.N(d.Gpu.ClockMhz, "N0")} MHz"),
            ("RAM", Id("Memory", SensorKind.Data, "Memory Used", "Used Memory"), $"{DashboardViews.N(d.Memory.UsedGb, "0.0")} GB"),
            (d.Fans.FirstOrDefault()?.Name.ToUpper(DashboardViews.C) ?? "FAN", d.Fans.FirstOrDefault()?.Id, d.Fans.FirstOrDefault() is { } f ? $"{f.Rpm.ToString("N0", DashboardViews.C)} rpm" : "—"),
            ("VRAM", Id("GPU", SensorKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"), $"{DashboardViews.N(d.Gpu.MemoryUsedMb / 1024, "0.0")} GB"),
        ];
        for (var i = 0; i < 9; i++)
        {
            _tiles[i].Label.Text = tiles[i].Label;
            _tiles[i].Value.Text = tiles[i].Value;
            _tiles[i].Chart.Capacity = hist.Capacity;
            _tiles[i].Chart.Set(tiles[i].Id is { } id ? [new ChartSeries(hist.Get(id), HudUi.Accent, Fill: true)] : []);
        }
    }
}
