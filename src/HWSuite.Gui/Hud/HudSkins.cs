using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace HWSuite.Gui.Hud;

/// <summary>
/// One look of the interface: the base Windows theme (light or dark controls), every HUD color, the accent colors
/// offered in Settings (each skin has its own shades, readable on its background) and the chart line colors.
/// </summary>
/// <param name="Id">Saved in the settings: "hud", "light", "metal".</param>
/// <param name="Dark">Dark controls (white text) or light ones (dark text).</param>
/// <param name="Colors">HUD and WinUI resource keys with their colors (#AARRGGBB).</param>
/// <param name="Accents">Accent id ("cyan", "violet"...) to color, in the order shown in Settings.</param>
/// <param name="Gradients">Keys painted with a gradient instead of a flat color (top-left to bottom-right stops).</param>
public sealed record HudSkin(
    string Id,
    bool Dark,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<(string Id, string Color)> Accents,
    IReadOnlyList<string> ChartColors,
    IReadOnlyDictionary<string, string[]>? Gradients = null)
{
    /// <summary>Title bar buttons: normal and inactive foreground.</summary>
    public string TitleButtons => Colors["HudTextBrush"];

    public string TitleButtonsInactive => Colors["HudTagBrush"];
}

/// <summary>Palette store: the colors in use this session. The palettes themselves belong to the themes (<see cref="HudThemes"/>).</summary>
public static class HudSkins
{
    /// <summary>The original dark HUD palette (kept for code that needs a fallback).</summary>
    public static HudSkin Hud => HudThemes.Hud.Dark;

    /// <summary>The skin applied at start (what the whole session shows).</summary>
    public static HudSkin Current { get; private set; } = HudThemes.Hud.Dark;

    public static Color Parse(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>
    /// Puts the skin's brushes into the HUD theme dictionary. Called once at start, before any page exists: brushes
    /// are replaced (not recolored), which is what lets a skin use gradients.
    /// </summary>
    public static void Apply(HudSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        Current = skin;
        foreach (var dictionary in HudAccents.ThemeDictionaries(Application.Current.Resources))
        {
            foreach (var (key, hex) in skin.Colors)
            {
                dictionary[key] = skin.Gradients is { } g && g.TryGetValue(key, out var stops) ? Gradient(stops) : new SolidColorBrush(Parse(hex));
            }
        }
    }

    private static LinearGradientBrush Gradient(string[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < stops.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop { Color = Parse(stops[i]), Offset = stops.Length == 1 ? 0 : (double)i / (stops.Length - 1) });
        }

        return brush;
    }
}
