using HWSuite.Core.Sensors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace HWSuite.Gui.Hud;

/// <summary>One line of a <see cref="TimeChart"/>.</summary>
public sealed record TimeSeries(string Name, IReadOnlyList<double> Values, Brush Stroke, SensorKind Kind);

/// <summary>
/// Line chart over real time (Charts page): value grid with labels, clock times along the bottom, one line per series
/// (thinned to the pixel width, peaks kept), and a crosshair that shows every value at the pointer's instant.
/// </summary>
public sealed partial class TimeChart : Grid
{
    private const double Left = 46, Bottom = 20, Top = 6;
    private readonly Canvas _canvas = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Line _cursor = new() { StrokeThickness = 1, StrokeDashArray = [2, 3], Visibility = Visibility.Collapsed };
    private readonly Border _tip = new() { Padding = new Thickness(10, 6, 10, 6), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1) };
    private readonly StackPanel _tipRows = new() { Spacing = 2 };
    private IReadOnlyList<DateTimeOffset> _times = [];
    private IReadOnlyList<TimeSeries> _series = [];
    private double _low, _high;

    public TimeChart()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // receives pointer events over empty space
        Children.Add(_canvas);
        Children.Add(_overlay);
        _tip.Child = _tipRows;
        _overlay.Children.Add(_cursor);
        _overlay.Children.Add(_tip);
        SizeChanged += (_, _) => Draw();
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => HideCursor();
    }

    /// <summary>Start the value axis at zero (fans, load, power).</summary>
    public bool FromZero { get; set; }

    public void Set(IReadOnlyList<DateTimeOffset> times, IReadOnlyList<TimeSeries> series)
    {
        _times = times;
        _series = series;
        Draw();
    }

    private double PlotWidth => Math.Max(1, ActualWidth - Left - 8);

    private double PlotHeight => Math.Max(1, ActualHeight - Bottom - Top);

    private void Draw()
    {
        _canvas.Children.Clear();
        if (ActualWidth < 60 || ActualHeight < 60)
        {
            return;
        }

        var c = App.Text.Culture;
        var finite = _series.SelectMany(s => s.Values).Where(double.IsFinite).ToList();
        (_low, _high) = finite.Count > 0 ? ChartReducer.NiceRange(finite.Min(), finite.Max(), 4, FromZero) : (0, 100);
        var grid = HudUi.Brush("HudAccentDimBrush");
        var mono = HudUi.Font("HudMonoFont");
        for (var i = 0; i <= 4; i++)
        {
            var y = Math.Round(Top + (PlotHeight * i / 4)) + 0.5;
            _canvas.Children.Add(new Line { X1 = Left, X2 = ActualWidth, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, StrokeDashArray = [3, 5] });
            var value = _high - ((_high - _low) * i / 4);
            Label(Core.Reporting.SvgChart.Value(value, _high - _low, c), 0, Math.Clamp(y - 8, 0, ActualHeight - 16), mono, Left - 6, TextAlignment.Right);
        }

        if (_times.Count < 2)
        {
            return;
        }

        var start = _times[0];
        var span = Math.Max(1e-9, (_times[^1] - start).TotalSeconds);
        var format = span > 6 * 3600 ? "dd/MM HH:mm" : span > 600 ? "HH:mm" : "HH:mm:ss";
        for (var i = 0; i <= 4; i++)
        {
            var x = Left + (PlotWidth * i / 4);
            var text = (start + TimeSpan.FromSeconds(span * i / 4)).ToLocalTime().ToString(format, c);
            var width = 90.0;
            var left = i == 0 ? x : i == 4 ? x - width : x - (width / 2);
            Label(text, left, ActualHeight - Bottom + 4, mono, width, i == 0 ? TextAlignment.Left : i == 4 ? TextAlignment.Right : TextAlignment.Center);
        }

        foreach (var s in _series)
        {
            var points = new PointCollection();
            foreach (var i in ChartReducer.Indexes(s.Values, (int)(PlotWidth / 2)))
            {
                points.Add(new Point(X(i, start, span), Y(s.Values[i])));
            }

            if (points.Count > 1)
            {
                _canvas.Children.Add(new Polyline { Points = points, Stroke = s.Stroke, StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round });
            }
        }
    }

    private double X(int index, DateTimeOffset start, double span) => Left + ((_times[index] - start).TotalSeconds / span * PlotWidth);

    private double Y(double value) => Top + PlotHeight - ((Math.Clamp(value, _low, _high) - _low) / (_high - _low) * PlotHeight);

    private void Label(string text, double left, double top, FontFamily font, double width, TextAlignment alignment)
    {
        var label = new TextBlock { Text = text, FontFamily = font, FontSize = 11, Foreground = HudUi.Tag, Width = width, TextAlignment = alignment };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        _canvas.Children.Add(label);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_times.Count < 2 || _series.Count == 0)
        {
            return;
        }

        var x = e.GetCurrentPoint(this).Position.X;
        var start = _times[0];
        var span = Math.Max(1e-9, (_times[^1] - start).TotalSeconds);
        var at = start + TimeSpan.FromSeconds(Math.Clamp((x - Left) / PlotWidth, 0, 1) * span);
        var index = Nearest(at);
        var cx = X(index, start, span);

        _cursor.Stroke = HudUi.Text;
        _cursor.X1 = _cursor.X2 = cx;
        _cursor.Y1 = Top;
        _cursor.Y2 = Top + PlotHeight;
        _cursor.Visibility = Visibility.Visible;

        var c = App.Text.Culture;
        _tipRows.Children.Clear();
        _tipRows.Children.Add(new TextBlock { Text = _times[index].ToLocalTime().ToString("HH:mm:ss", c), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 11, Foreground = HudUi.Tag });
        foreach (var s in _series)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Rectangle { Width = 10, Height = 3, Fill = s.Stroke, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = SensorFormat.Format(s.Values[index], s.Kind, c), FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 12, Foreground = HudUi.Text, MinWidth = 70 });
            row.Children.Add(new TextBlock { Text = s.Name, FontSize = 12, Foreground = HudUi.TextDim, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis });
            _tipRows.Children.Add(row);
        }

        _tip.Background = HudUi.Brush("HudPanelStrongBrush");
        _tip.BorderBrush = HudUi.AccentDim;
        _tip.Visibility = Visibility.Visible;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipW = _tip.DesiredSize.Width;
        Canvas.SetLeft(_tip, cx + 12 + tipW > ActualWidth ? Math.Max(0, cx - 12 - tipW) : cx + 12);
        Canvas.SetTop(_tip, Top + 4);
    }

    private int Nearest(DateTimeOffset at)
    {
        int lo = 0, hi = _times.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_times[mid] < at)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo > 0 && (at - _times[lo - 1]) < (_times[lo] - at) ? lo - 1 : lo;
    }

    private void HideCursor()
    {
        _cursor.Visibility = Visibility.Collapsed;
        _tip.Visibility = Visibility.Collapsed;
    }
}
