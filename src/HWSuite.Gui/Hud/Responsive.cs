using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Hud;

/// <summary>
/// Reflows a grid of panels to the width it has: its columns stay as designed while each gets at least
/// <c>minColumn</c> pixels, otherwise the panels wrap onto more rows (down to one per row). The grid's original
/// layout is remembered on the first call, so it can widen again.
/// </summary>
public static class Responsive
{
    private sealed record Layout(List<GridLength> Columns, List<(FrameworkElement Child, int Span)> Children);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Grid, Layout> Originals = [];

    public static void Reflow(Grid grid, double width, double minColumn)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (width <= 0)
        {
            return;
        }

        var layout = Originals.GetValue(grid, g => new Layout(
            [.. g.ColumnDefinitions.Select(c => c.Width)],
            [.. g.Children.OfType<FrameworkElement>()
                .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn)
                .Select(c => (c, Math.Max(1, Grid.GetColumnSpan(c))))]));
        var designed = layout.Columns.Count;
        if (designed < 2)
        {
            return;
        }

        var columns = Math.Clamp((int)Math.Floor((width + grid.ColumnSpacing) / (minColumn + grid.ColumnSpacing)), 1, designed);
        if (grid.ColumnDefinitions.Count == columns && grid.Tag is int done && done == columns)
        {
            return;
        }

        grid.Tag = columns;
        grid.ColumnDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            // Designed proportions when nothing wraps, equal columns otherwise.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = columns == designed ? layout.Columns[i] : new GridLength(1, GridUnitType.Star) });
        }

        if (columns == designed)
        {
            foreach (var c in grid.ColumnDefinitions)
            {
                c.MinWidth = 0;
            }
        }

        int row = 0, col = 0;
        foreach (var (child, span) in layout.Children)
        {
            var s = Math.Min(span, columns);
            if (col + s > columns)
            {
                row++;
                col = 0;
            }

            Grid.SetRow(child, row);
            Grid.SetColumn(child, col);
            Grid.SetColumnSpan(child, s);
            col += s;
        }

        grid.RowDefinitions.Clear();
        for (var r = 0; r <= row; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        if (grid.RowSpacing < grid.ColumnSpacing)
        {
            grid.RowSpacing = grid.ColumnSpacing;
        }
    }

    /// <summary>Reflows every grid tagged with <see cref="MinColumnProperty"/> under <paramref name="root"/>.</summary>
    public static void ReflowAll(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Grid g && g.ActualWidth > 0 && GetMinColumn(g) is var min && min > 0)
            {
                Reflow(g, g.ActualWidth, min);
            }

            ReflowAll(child);
        }
    }

    public static readonly DependencyProperty MinColumnProperty =
        DependencyProperty.RegisterAttached("MinColumn", typeof(double), typeof(Responsive), new PropertyMetadata(0.0, OnMinColumnChanged));

    private static void OnMinColumnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid && e.OldValue is 0.0)
        {
            grid.SizeChanged += (_, args) => Reflow(grid, args.NewSize.Width, GetMinColumn(grid));
        }
    }

    public static double GetMinColumn(DependencyObject o) => (double)o.GetValue(MinColumnProperty);

    public static void SetMinColumn(DependencyObject o, double value) => o.SetValue(MinColumnProperty, value);
}
