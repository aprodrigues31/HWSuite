using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HWSuite.Gui.Hud;

/// <summary>
/// A translucent panel with the top-left and bottom-right corners cut at 45°, the HUD's basic container.
/// <see cref="Highlight"/> draws accent brackets on the cut corners (the panel that matters most on a page).
/// </summary>
public sealed partial class HudPanel : ContentControl
{
    public static readonly DependencyProperty ChamferProperty =
        DependencyProperty.Register(nameof(Chamfer), typeof(double), typeof(HudPanel), new PropertyMetadata(14.0, (d, _) => ((HudPanel)d).Redraw()));

    public static readonly DependencyProperty HighlightProperty =
        DependencyProperty.Register(nameof(Highlight), typeof(bool), typeof(HudPanel), new PropertyMetadata(false, (d, _) => ((HudPanel)d).Redraw()));

    private Path? _shape;
    private Path? _corner;

    public HudPanel()
    {
        DefaultStyleKey = typeof(HudPanel);
        SizeChanged += (_, _) => Redraw();
    }

    public double Chamfer
    {
        get => (double)GetValue(ChamferProperty);
        set => SetValue(ChamferProperty, value);
    }

    public bool Highlight
    {
        get => (bool)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shape = GetTemplateChild("PART_Shape") as Path;
        _corner = GetTemplateChild("PART_Corner") as Path;
        Redraw();
    }

    private void Redraw()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (_shape is null || w < 2 || h < 2)
        {
            return;
        }

        // Half-pixel inset so the 1 px outline is crisp and inside the bounds.
        const double I = 0.5;
        var theme = HudThemes.Current;
        if (theme.Shape != PanelShape.Chamfer)
        {
            var radius = theme.Shape == PanelShape.Rounded ? Math.Min(theme.Radius, Math.Min(w, h) / 2) : 0;
            _shape.Data = new RectangleGeometry { Rect = new Rect(I, I, w - (2 * I), h - (2 * I)) };
            if (radius > 0)
            {
                _shape.Data = RoundedRect(I, I, w - I, h - I, radius);
            }

            if (_corner is not null)
            {
                // Hologram: bright brackets on two corners of every panel; others: none.
                var brackets = theme.Shape == PanelShape.Bracket;
                _corner.Opacity = brackets || (Highlight && theme.Shape == PanelShape.Square) ? 1 : 0;
                if (_corner.Opacity > 0)
                {
                    const double L = 14;
                    var group = new GeometryGroup();
                    group.Children.Add(Polyline(new Point(1, L), new Point(1, 1), new Point(L, 1)));
                    group.Children.Add(Polyline(new Point(w - 1, h - L), new Point(w - 1, h - 1), new Point(w - L, h - 1)));
                    _corner.Data = group;
                }
            }

            return;
        }

        var c = Math.Min(Chamfer, Math.Min(w, h) / 3);
        _shape.Data = Polygon(
            new Point(c, I), new Point(w - I, I), new Point(w - I, h - c), new Point(w - c, h - I), new Point(I, h - I), new Point(I, c));

        if (_corner is not null)
        {
            _corner.Opacity = Highlight ? 1 : 0;
            if (Highlight)
            {
                var len = c + 10;
                var group = new GeometryGroup();
                group.Children.Add(Polyline(new Point(1, len), new Point(1, c), new Point(c, 1), new Point(len + c, 1)));
                group.Children.Add(Polyline(new Point(w - 1, h - len), new Point(w - 1, h - c), new Point(w - c, h - 1), new Point(w - len - c, h - 1)));
                _corner.Data = group;
            }
        }
    }

    private static PathGeometry RoundedRect(double x0, double y0, double x1, double y1, double r)
    {
        var figure = new PathFigure { StartPoint = new Point(x0 + r, y0), IsClosed = true, IsFilled = true };
        void Arc(double x, double y) => figure.Segments.Add(new ArcSegment { Point = new Point(x, y), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = new Point(x1 - r, y0) });
        Arc(x1, y0 + r);
        figure.Segments.Add(new LineSegment { Point = new Point(x1, y1 - r) });
        Arc(x1 - r, y1);
        figure.Segments.Add(new LineSegment { Point = new Point(x0 + r, y1) });
        Arc(x0, y1 - r);
        figure.Segments.Add(new LineSegment { Point = new Point(x0, y0 + r) });
        Arc(x0 + r, y0);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        foreach (var p in points.Skip(1))
        {
            figure.Segments.Add(new LineSegment { Point = p });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry Polyline(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        foreach (var p in points.Skip(1))
        {
            figure.Segments.Add(new LineSegment { Point = p });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

/// <summary>The faint square grid behind every page (lines every 32 px), redrawn when the window is resized.</summary>
public sealed partial class GridBackdrop : Canvas
{
    private const double Step = 32;
    private readonly Path _lines = new() { StrokeThickness = 1 };

    public GridBackdrop()
    {
        IsHitTestVisible = false;
        Visibility = HudThemes.Current.Grid ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_lines);
        Loaded += (_, _) => _lines.Stroke = HudUi.Brush("HudGridLineBrush");
        SizeChanged += (_, e) => Redraw(e.NewSize);
    }

    private void Redraw(Size size)
    {
        var group = new GeometryGroup();
        for (var x = Step; x < size.Width; x += Step)
        {
            group.Children.Add(new LineGeometry { StartPoint = new Point(x + 0.5, 0), EndPoint = new Point(x + 0.5, size.Height) });
        }

        for (var y = Step; y < size.Height; y += Step)
        {
            group.Children.Add(new LineGeometry { StartPoint = new Point(0, y + 0.5), EndPoint = new Point(size.Width, y + 0.5) });
        }

        _lines.Data = group;
    }
}
