using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace HWSuite.Gui.Hud;

/// <summary>
/// Lays its visible children out in as many columns as fit (each at least <see cref="MinColumnWidth"/> wide, never
/// more columns than visible children), every child going to the currently shortest column. Hiding children (a
/// filter) or resizing the window re-flows the rest, so one visible block takes the whole width and several share it.
/// </summary>
public sealed partial class MasonryPanel : Panel
{
    /// <summary>
    /// Re-flows the blocks now. Pages call it after showing or hiding children: a visibility change alone does not
    /// always make WinUI measure a custom panel again.
    /// </summary>
    public void Reflow()
    {
        InvalidateMeasure();
        InvalidateArrange();
        UpdateLayout();
    }

    public double MinColumnWidth { get; set; } = 480;

    public double Spacing { get; set; } = 16;

    /// <summary>Upper limit of columns (0 = no limit).</summary>
    public int MaxColumns { get; set; }

    private int Columns(double width, int visible)
    {
        if (visible == 0 || double.IsInfinity(width))
        {
            return 1;
        }

        var fit = (int)Math.Floor((width + Spacing) / (MinColumnWidth + Spacing));
        var columns = Math.Clamp(fit, 1, visible);
        return MaxColumns > 0 ? Math.Min(columns, MaxColumns) : columns;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        var columns = Columns(availableSize.Width, visible.Count);
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : (availableSize.Width - ((columns - 1) * Spacing)) / columns;
        var heights = new double[columns];
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            child.Measure(new Size(width, double.PositiveInfinity));
            var shortest = Array.IndexOf(heights, heights.Min());
            heights[shortest] += child.DesiredSize.Height + Spacing;
        }

        var height = heights.Length == 0 ? 0 : Math.Max(0, heights.Max() - Spacing);
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Count(c => c.Visibility == Visibility.Visible);
        var columns = Columns(finalSize.Width, visible);
        var width = (finalSize.Width - ((columns - 1) * Spacing)) / columns;
        var heights = new double[columns];
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var shortest = Array.IndexOf(heights, heights.Min());
            child.Arrange(new Rect(shortest * (width + Spacing), heights[shortest], width, child.DesiredSize.Height));
            heights[shortest] += child.DesiredSize.Height + Spacing;
        }

        return finalSize;
    }
}
