using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.UI;

namespace HWSuite.Gui.Hud;

/// <summary>
/// A soft neon glow behind a shape or a text: a composition drop shadow (no offset) masked by the element's
/// own pixels, drawn on a host element placed right behind it at the same position. Purely decorative: if
/// composition is unavailable the element simply has no glow.
/// </summary>
public static class Glow
{
    public static void Attach(FrameworkElement host, UIElement target, Brush color, float blur = 12, float opacity = 0.85f)
    {
        try
        {
            var mask = target switch
            {
                Shape s => s.GetAlphaMask(),
                TextBlock t => t.GetAlphaMask(),
                _ => null,
            };
            if (mask is null)
            {
                return;
            }

            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = blur;
            shadow.Opacity = opacity;
            shadow.Mask = mask;
            shadow.Offset = Vector3.Zero;
            shadow.Color = color is SolidColorBrush b ? b.Color : Color.FromArgb(255, 0x4F, 0xE3, 0xFF);
            if (color is SolidColorBrush sb)
            {
                sb.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => shadow.Color = sb.Color);
            }

            var sprite = compositor.CreateSpriteVisual();
            sprite.Shadow = shadow;
            void Size() => sprite.Size = new Vector2((float)((FrameworkElement)target).ActualWidth, (float)((FrameworkElement)target).ActualHeight);
            ((FrameworkElement)target).SizeChanged += (_, _) => Size();
            Size();
            ElementCompositionPreview.SetElementChildVisual(host, sprite);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // no glow
        }
    }
}

/// <summary>A text that glows in its own color (big live values).</summary>
public sealed partial class GlowText : Grid
{
    private readonly Canvas _host = new() { IsHitTestVisible = false };
    private bool _attached;

    public GlowText()
    {
        Text = new TextBlock();
        Children.Add(_host);
        Children.Add(Text);
        Loaded += (_, _) =>
        {
            if (!_attached)
            {
                _attached = true;
                Glow.Attach(_host, Text, Text.Foreground ?? HudUi.Accent, 14, 0.7f);
            }
        };
    }

    public TextBlock Text { get; }
}

/// <summary>
/// The "reactor" gauge: a ring of segments lit in proportion to <see cref="Value"/> (0..1), an optional thin
/// inner arc for a second value (amber), and three lines of text in the middle.
/// </summary>
public sealed partial class RingGauge : Grid
{
    private const int Count = 48;
    private readonly Path _dim = new() { Stretch = Stretch.None };
    private readonly Path _lit = new() { Stretch = Stretch.None };
    private readonly Path _secondary = new() { Stretch = Stretch.None, StrokeThickness = 3 };
    private readonly Ellipse _outer = new() { StrokeThickness = 1 };
    private readonly Ellipse _inner = new() { StrokeThickness = 1, StrokeDashArray = [2, 6] };
    private readonly Canvas _canvas = new();
    private readonly Canvas _glowHost = new();
    private readonly TextBlock _caption = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly GlowText _value = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _sub = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private double _fraction;
    private double? _secondaryFraction;
    private bool _warn;

    public RingGauge()
    {
        _canvas.Children.Add(_outer);
        _canvas.Children.Add(_inner);
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_glowHost);
        _canvas.Children.Add(_lit);
        _canvas.Children.Add(_secondary);
        Children.Add(_canvas);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0 };
        center.Children.Add(_caption);
        center.Children.Add(_value);
        center.Children.Add(_sub);
        Children.Add(center);
        Loaded += (_, _) =>
        {
            _caption.Style = HudUi.Style("HudTagStyle");
            _value.Text.FontFamily = HudUi.Font("HudMonoStrongFont");
            _value.Text.Foreground = HudUi.Text;
            _sub.FontFamily = HudUi.Font("HudMonoFont");
            _sub.FontSize = 11;
            _sub.Foreground = HudUi.Warn;
            Layout();
            Glow.Attach(_glowHost, _lit, HudUi.Accent, 10, 0.9f);
        };
    }

    public double Diameter { get; set; } = 220;

    public void Set(double fraction, string caption, string value, string sub, double? secondary = null, bool warn = false)
    {
        _caption.Text = caption;
        _value.Text.Text = value;
        _sub.Text = sub;
        var changed = Math.Abs(fraction - _fraction) > 0.002 || secondary != _secondaryFraction || warn != _warn;
        _fraction = Math.Clamp(fraction, 0, 1);
        _secondaryFraction = secondary;
        _warn = warn;
        if (changed)
        {
            Layout();
        }
    }

    private void Layout()
    {
        var d = Diameter;
        Width = Height = d;
        _canvas.Width = _canvas.Height = _glowHost.Width = _glowHost.Height = d;
        var c = d / 2;
        var thickness = Math.Round(d * 0.062);
        var r = c - 10 - (thickness / 2);
        _value.Text.FontSize = Math.Round(d * 0.18);

        _outer.Width = _outer.Height = d - 2;
        Canvas.SetLeft(_outer, 1);
        Canvas.SetTop(_outer, 1);
        _outer.Stroke = HudUi.AccentDim;
        var innerR = r - (thickness / 2) - 12;
        _inner.Width = _inner.Height = innerR * 2;
        Canvas.SetLeft(_inner, c - innerR);
        Canvas.SetTop(_inner, c - innerR);
        _inner.Stroke = HudUi.AccentDim;

        var dash = Geo.Segments(r, 360, Count, thickness);
        foreach (var p in new[] { _dim, _lit })
        {
            p.Width = p.Height = d;
            p.StrokeThickness = thickness;
            p.StrokeDashArray = [.. dash];
        }

        _dim.Stroke = HudUi.Brush("HudAccentFaintBrush");
        _dim.Data = Geo.Arc(c, c, r, -90, 360);
        _lit.Stroke = _warn ? HudUi.Warn : HudUi.Accent;

        // Whole segments only, so the lit part never ends in half a segment.
        var lit = Math.Round(_fraction * Count) / Count;
        _lit.Data = Geo.Arc(c, c, r, -90, lit * 360);
        _lit.Visibility = lit > 0 ? Visibility.Visible : Visibility.Collapsed;

        _secondary.Width = _secondary.Height = d;
        _secondary.Stroke = HudUi.Warn;
        _secondary.Visibility = _secondaryFraction is > 0 ? Visibility.Visible : Visibility.Collapsed;
        _secondary.Data = Geo.Arc(c, c, innerR - 6, -90, Math.Clamp(_secondaryFraction ?? 0, 0, 1) * 360);
    }
}

/// <summary>
/// Speedometer: a 270° arc of segments from bottom-left to bottom-right with scale labels, the value in the
/// middle and an optional inner arc for a second value (upload under download).
/// </summary>
public sealed partial class ArcGauge : Grid
{
    private const int Count = 40;
    private const double Start = 135;
    private const double Sweep = 270;
    private readonly Path _dim = new();
    private readonly Path _lit = new();
    private readonly Path _secondaryTrack = new() { StrokeThickness = 4 };
    private readonly Path _secondary = new() { StrokeThickness = 4 };
    private readonly Canvas _canvas = new();
    private readonly Canvas _glowHost = new();
    private readonly TextBlock _min = new();
    private readonly TextBlock _mid = new();
    private readonly TextBlock _max = new();
    private readonly TextBlock _caption = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly GlowText _value = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _unit = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private double _fraction;
    private double? _secondaryFraction;

    public ArcGauge()
    {
        foreach (var e in new UIElement[] { _dim, _glowHost, _lit, _secondaryTrack, _secondary, _min, _mid, _max })
        {
            _canvas.Children.Add(e);
        }

        Children.Add(_canvas);
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        center.Children.Add(_caption);
        center.Children.Add(_value);
        center.Children.Add(_unit);
        Children.Add(center);
        Loaded += (_, _) =>
        {
            _caption.Style = HudUi.Style("HudTagStyle");
            _value.Text.FontFamily = HudUi.Font("HudMonoStrongFont");
            _value.Text.Foreground = HudUi.Text;
            _unit.FontFamily = HudUi.Font("HudMonoFont");
            _unit.FontSize = 12;
            _unit.Foreground = HudUi.Tag;
            foreach (var t in new[] { _min, _mid, _max })
            {
                t.FontFamily = HudUi.Font("HudMonoFont");
                t.FontSize = 10;
                t.Foreground = HudUi.Tag;
            }

            Layout();
            Glow.Attach(_glowHost, _lit, HudUi.Accent, 10, 0.9f);
        };
    }

    public double Diameter { get; set; } = 300;

    public void Set(double fraction, string caption, string value, string unit, string min, string mid, string max, double? secondary = null)
    {
        _caption.Text = caption;
        _value.Text.Text = value;
        _unit.Text = unit;
        _min.Text = min;
        _mid.Text = mid;
        _max.Text = max;
        _fraction = Math.Clamp(fraction, 0, 1);
        _secondaryFraction = secondary;
        Layout();
    }

    private void Layout()
    {
        var d = Diameter;
        var c = d / 2;
        Width = d;
        Height = d * 0.86;
        _canvas.Width = _glowHost.Width = d;
        _canvas.Height = _glowHost.Height = d;
        var thickness = Math.Round(d * 0.06);
        var r = c - 14 - (thickness / 2);
        _value.Text.FontSize = Math.Round(d * 0.17);

        var dash = Geo.Segments(r, Sweep, Count, thickness);
        foreach (var p in new[] { _dim, _lit })
        {
            p.Width = p.Height = d;
            p.StrokeThickness = thickness;
            p.StrokeDashArray = [.. dash];
        }

        _dim.Stroke = HudUi.Brush("HudAccentFaintBrush");
        _dim.Data = Geo.Arc(c, c, r, Start, Sweep);
        _lit.Stroke = HudUi.Accent;
        var lit = Math.Round(_fraction * Count) / Count;
        _lit.Data = Geo.Arc(c, c, r, Start, lit * Sweep);
        _lit.Visibility = lit > 0 ? Visibility.Visible : Visibility.Collapsed;

        var inner = r - (thickness / 2) - 12;
        foreach (var p in new[] { _secondaryTrack, _secondary })
        {
            p.Width = p.Height = d;
        }

        _secondaryTrack.Stroke = HudUi.Brush("HudWarnDimBrush");
        _secondaryTrack.Data = Geo.Arc(c, c, inner, Start, Sweep);
        _secondary.Stroke = HudUi.Warn;
        _secondary.Data = Geo.Arc(c, c, inner, Start, Math.Clamp(_secondaryFraction ?? 0, 0, 1) * Sweep);
        _secondaryTrack.Visibility = _secondary.Visibility = _secondaryFraction is null ? Visibility.Collapsed : Visibility.Visible;

        Place(_min, Geo.OnCircle(c, c, r + 4, Start), -6, 8);
        Place(_mid, new Point(c, 0), 0, -2, center: true);
        Place(_max, Geo.OnCircle(c, c, r + 4, Start + Sweep), -18, 8);
    }

    private static void Place(TextBlock t, Point p, double dx, double dy, bool center = false)
    {
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(t, p.X + dx - (center ? t.DesiredSize.Width / 2 : 0));
        Canvas.SetTop(t, p.Y + dy);
    }
}

/// <summary>
/// A horizontal bar made of small blocks, the lit ones showing <see cref="Value"/> (0..1). Blocks are created
/// once per width and only recolored on updates (the sensor list has dozens of these refreshing every second).
/// </summary>
public sealed partial class SegmentBar : Canvas
{
    private const double Block = 6;
    private const double Gap = 3;
    private readonly List<Rectangle> _blocks = [];
    private double _value;
    private Brush? _fill;

    public SegmentBar()
    {
        Height = 8;
        SizeChanged += (_, e) => Build(e.NewSize);
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            Paint();
        }
    }

    /// <summary>Lit color; null means the accent.</summary>
    public Brush? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            Paint();
        }
    }

    private void Build(Size size)
    {
        var count = (int)Math.Max(1, Math.Floor((size.Width + Gap) / (Block + Gap)));
        if (count == _blocks.Count && size.Height == (_blocks.Count > 0 ? _blocks[0].Height : -1))
        {
            return;
        }

        Children.Clear();
        _blocks.Clear();
        for (var i = 0; i < count; i++)
        {
            var rect = new Rectangle { Width = Block, Height = size.Height };
            SetLeft(rect, i * (Block + Gap));
            _blocks.Add(rect);
            Children.Add(rect);
        }

        Paint();
    }

    private void Paint()
    {
        if (_blocks.Count == 0)
        {
            return;
        }

        var lit = (int)Math.Round(_value * _blocks.Count);
        var on = _fill ?? HudUi.Accent;
        var off = HudUi.Brush("HudAccentFaintBrush");
        for (var i = 0; i < _blocks.Count; i++)
        {
            var brush = i < lit ? on : off;
            if (!ReferenceEquals(_blocks[i].Fill, brush))
            {
                _blocks[i].Fill = brush;
            }
        }
    }
}

/// <summary>One line of a <see cref="LineChart"/>: values oldest first, its color, and whether the area under it is tinted.</summary>
public sealed record ChartSeries(IReadOnlyList<double> Values, Brush Stroke, bool Fill = false, bool Dashed = false);

/// <summary>
/// Line chart for live history: dashed grid lines with their values, newest sample at the right edge
/// (<see cref="Capacity"/> samples across the width), optional tinted area under a line.
/// </summary>
public sealed partial class LineChart : Grid
{
    private readonly Canvas _canvas = new();
    private readonly Canvas _glowHost = new();
    private IReadOnlyList<ChartSeries> _series = [];

    public LineChart()
    {
        Children.Add(_glowHost);
        Children.Add(_canvas);
        SizeChanged += (_, _) => Draw();
    }

    /// <summary>Samples that fill the width (the history length); fewer samples start further right.</summary>
    public int Capacity { get; set; } = 300;

    /// <summary>Fixed vertical range; when null the range follows the data.</summary>
    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public string Unit { get; set; } = "";

    public int GridLines { get; set; } = 3;

    public void Set(IReadOnlyList<ChartSeries> series)
    {
        _series = series;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 20 || h < 20)
        {
            return;
        }

        var values = _series.SelectMany(s => s.Values).Where(double.IsFinite).ToList();
        var low = Minimum ?? (values.Count > 0 ? Math.Floor(values.Min() / 5) * 5 - 5 : 0);
        var high = Maximum ?? (values.Count > 0 ? Math.Ceiling(values.Max() / 5) * 5 + 5 : 100);
        if (high - low < 1e-6)
        {
            high = low + 1;
        }

        const double Left = 34;
        var plotW = w - Left;
        var grid = HudUi.Brush("HudAccentDimBrush");
        for (var i = 0; i <= GridLines; i++)
        {
            var y = Math.Round(h * i / GridLines) + 0.5;
            _canvas.Children.Add(new Line { X1 = Left, X2 = w, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, StrokeDashArray = [3, 5] });
            var label = new TextBlock
            {
                Text = (high - ((high - low) * i / GridLines)).ToString("0", App.Text.Culture),
                FontFamily = HudUi.Font("HudMonoFont"),
                FontSize = 10,
                Foreground = HudUi.Tag,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Clamp(y - 7, 0, h - 14));
            _canvas.Children.Add(label);
        }

        var step = plotW / Math.Max(1, Capacity - 1);
        foreach (var s in _series)
        {
            var points = new PointCollection();
            var offset = Capacity - s.Values.Count;
            for (var i = 0; i < s.Values.Count; i++)
            {
                if (double.IsFinite(s.Values[i]))
                {
                    var x = Left + ((offset + i) * step);
                    var y = h - ((Math.Clamp(s.Values[i], low, high) - low) / (high - low) * h);
                    points.Add(new Point(x, y));
                }
            }

            if (points.Count < 2)
            {
                continue;
            }

            if (s.Fill && s.Stroke is SolidColorBrush solid)
            {
                var area = new Polygon { Fill = new SolidColorBrush(HudAccents.WithAlpha(solid.Color, 0x16)) };
                area.Points.Add(new Point(points[0].X, h));
                foreach (var p in points)
                {
                    area.Points.Add(p);
                }

                area.Points.Add(new Point(points[^1].X, h));
                _canvas.Children.Add(area);
            }

            var line = new Polyline { Points = points, Stroke = s.Stroke, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            if (s.Dashed)
            {
                line.StrokeDashArray = [4, 3];
            }

            _canvas.Children.Add(line);
        }
    }
}

/// <summary>Vertical bars side by side (load of each CPU thread), bottom-aligned, 0–100.</summary>
public sealed partial class ColumnBars : Grid
{
    private readonly List<Rectangle> _bars = [];

    public ColumnBars()
    {
        ColumnSpacing = 3;
        Height = 34;
    }

    public void Set(IReadOnlyList<double> percents)
    {
        if (percents.Count != _bars.Count)
        {
            Children.Clear();
            ColumnDefinitions.Clear();
            _bars.Clear();
            for (var i = 0; i < percents.Count; i++)
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var track = new Rectangle { Fill = HudUi.Brush("HudAccentFaintBrush") };
                var bar = new Rectangle { VerticalAlignment = VerticalAlignment.Bottom, Fill = HudUi.Accent };
                SetColumn(track, i);
                SetColumn(bar, i);
                Children.Add(track);
                Children.Add(bar);
                _bars.Add(bar);
            }
        }

        for (var i = 0; i < percents.Count; i++)
        {
            _bars[i].Height = Math.Max(1, Math.Clamp(percents[i], 0, 100) / 100 * Height);
        }
    }
}
