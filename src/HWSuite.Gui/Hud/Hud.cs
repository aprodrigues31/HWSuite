using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace HWSuite.Gui.Hud;

/// <summary>The accent colors offered in Settings (the HUD's one bright color).</summary>
public static class HudAccents
{
    public static readonly IReadOnlyList<(string Id, Color Color)> All =
    [
        ("cyan", Color.FromArgb(255, 0x4F, 0xE3, 0xFF)),
        ("violet", Color.FromArgb(255, 0x9D, 0x7C, 0xFF)),
        ("green", Color.FromArgb(255, 0x39, 0xF5, 0xB0)),
        ("magenta", Color.FromArgb(255, 0xFF, 0x4F, 0xD8)),
        ("amber", Color.FromArgb(255, 0xFF, 0xB5, 0x47)),
    ];

    public static Color Of(string? id) => All.FirstOrDefault(a => a.Id == id).Color is { A: > 0 } c ? c : All[0].Color;

    /// <summary>
    /// Recolors the accent everywhere: the HUD brushes (changed in place, so what is on screen follows) and the
    /// system accent colors the standard controls use (those apply to controls created afterwards).
    /// </summary>
    public static void Apply(string? id)
    {
        var color = Of(id);
        var resources = Application.Current.Resources;
        foreach (var dictionary in Dictionaries(resources))
        {
            SetBrush(dictionary, "HudAccentBrush", color);
            SetBrush(dictionary, "HudAccentDimBrush", WithAlpha(color, 0x26));
            SetBrush(dictionary, "HudAccentFaintBrush", WithAlpha(color, 0x12));
            SetBrush(dictionary, "HudPanelStrokeBrush", WithAlpha(color, 0x3A));
            SetBrush(dictionary, "HudGridLineBrush", WithAlpha(color, 0x0C));
            SetBrush(dictionary, "HudHoverBrush", WithAlpha(color, 0x0F));
            SetBrush(dictionary, "HudSelectedBrush", WithAlpha(color, 0x17));
            SetBrush(dictionary, "CardStrokeColorDefaultBrush", WithAlpha(color, 0x3A));
            SetBrush(dictionary, "DividerStrokeColorDefaultBrush", WithAlpha(color, 0x26));
            SetBrush(dictionary, "ControlStrokeColorDefaultBrush", WithAlpha(color, 0x40));
            SetBrush(dictionary, "SystemFillColorAttentionBrush", color);
            dictionary["SystemAccentColor"] = color;
            dictionary["SystemAccentColorLight1"] = Mix(color, Colors.White, 0.25);
            dictionary["SystemAccentColorLight2"] = color;
            dictionary["SystemAccentColorLight3"] = Mix(color, Colors.White, 0.5);
            dictionary["SystemAccentColorDark1"] = Mix(color, Colors.Black, 0.2);
            dictionary["SystemAccentColorDark2"] = Mix(color, Colors.Black, 0.4);
            dictionary["SystemAccentColorDark3"] = Mix(color, Colors.Black, 0.6);
        }
    }

    private static IEnumerable<ResourceDictionary> Dictionaries(ResourceDictionary root)
    {
        foreach (var merged in root.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue("Default", out var d) && d is ResourceDictionary theme && theme.ContainsKey("HudAccentBrush"))
            {
                yield return theme;
            }
        }
    }

    private static void SetBrush(ResourceDictionary d, string key, Color color)
    {
        if (d.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));
}

/// <summary>Shared resources and small builders for the HUD look, used by pages that build UI in code.</summary>
public static class HudUi
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static FontFamily Font(string key) => (FontFamily)Application.Current.Resources[key];

    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static Brush Accent => Brush("HudAccentBrush");

    public static Brush AccentDim => Brush("HudAccentDimBrush");

    public static Brush Warn => Brush("HudWarnBrush");

    public static Brush Crit => Brush("HudCritBrush");

    public static Brush Text => Brush("HudTextBrush");

    public static Brush TextDim => Brush("HudTextDimBrush");

    public static Brush Tag => Brush("HudTagBrush");

    /// <summary>"// SECTION" label (upper case, wide tracking, mono).</summary>
    public static TextBlock TagText(string text, CultureInfo culture) => new() { Text = text.ToUpper(culture), Style = Style("HudTagStyle") };

    /// <summary>"917 Mbps": the number in the strong mono font, the unit smaller and dimmer.</summary>
    public static TextBlock ValueText(string value, string unit, double size = 22, Brush? color = null)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        text.Inlines.Add(new Run { Text = value, FontFamily = Font("HudMonoStrongFont"), FontSize = size, Foreground = color ?? Text });
        if (unit.Length > 0)
        {
            text.Inlines.Add(new Run { Text = " " + unit, FontFamily = Font("HudMonoFont"), FontSize = Math.Max(11, size * 0.5), Foreground = Tag });
        }

        return text;
    }

    /// <summary>A chamfered panel with an optional "// TITLE" label above its content.</summary>
    public static HudPanel Panel(string? title, UIElement content, CultureInfo culture)
    {
        if (title is null)
        {
            return new HudPanel { Content = content };
        }

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(TagText("// " + title, culture));
        stack.Children.Add(content);
        return new HudPanel { Content = stack };
    }

    /// <summary>Label on the left, value on the right, a dashed line under it (lists of facts).</summary>
    public static Grid FactRow(string label, FrameworkElement value)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 7, 0, 7), BorderBrush = Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, Foreground = TextDim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        value.HorizontalAlignment = HorizontalAlignment.Right;

        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    public static TextBlock Mono(string text, Brush? color = null, double size = 13) =>
        new() { Text = text, FontFamily = Font("HudMonoFont"), FontSize = size, Foreground = color ?? Text, VerticalAlignment = VerticalAlignment.Center };

    public static TextBlock Strong(string text, double size = 15) =>
        new() { Text = text, FontFamily = Font("HudDisplayFont"), FontSize = size, CharacterSpacing = 30, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis };
}

/// <summary>Arc geometry for the gauges: angles in degrees, clockwise, 0° pointing right (screen coordinates).</summary>
public static class Geo
{
    public static Point OnCircle(double cx, double cy, double r, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a)));
    }

    public static Geometry Arc(double cx, double cy, double r, double startDegrees, double sweepDegrees)
    {
        sweepDegrees = Math.Clamp(sweepDegrees, 0, 359.95);
        var figure = new PathFigure { StartPoint = OnCircle(cx, cy, r, startDegrees), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = OnCircle(cx, cy, r, startDegrees + sweepDegrees),
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>Dash pattern (in stroke-thickness units) that splits an arc into segments with gaps.</summary>
    public static DoubleCollection Segments(double radius, double sweepDegrees, int count, double thickness, double fill = 0.62)
    {
        var length = 2 * Math.PI * radius * sweepDegrees / 360;
        var period = length / count;
        return [period * fill / thickness, period * (1 - fill) / thickness];
    }
}
