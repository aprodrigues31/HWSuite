using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace HWSuite.Gui.Hud;

/// <summary>Children left to right, wrapping to a new line when the width runs out (chips).</summary>
public sealed partial class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;

    public double LineSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, line = 0, widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                y += line + LineSpacing;
                (x, line) = (0, 0);
            }

            x += size.Width + Spacing;
            line = Math.Max(line, size.Height);
            widest = Math.Max(widest, x - Spacing);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : availableSize.Width, y + line);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, line = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += line + LineSpacing;
                (x, line) = (0, 0);
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            line = Math.Max(line, size.Height);
        }

        return finalSize;
    }
}
