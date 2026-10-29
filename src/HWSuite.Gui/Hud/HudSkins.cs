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

/// <summary>The skins offered in Settings. The chosen one is applied at start, before the window exists.</summary>
public static class HudSkins
{
    /// <summary>The original look: near-black background, translucent panels, one bright accent.</summary>
    public static readonly HudSkin Hud = new(
        "hud",
        Dark: true,
        new Dictionary<string, string>
        {
            ["HudWarnBrush"] = "#FFFFB547", ["HudWarnDimBrush"] = "#2EFFB547", ["HudCritBrush"] = "#FFFF4F6E", ["HudGoodBrush"] = "#FF39F5B0",
            ["HudBackgroundBrush"] = "#FF04070B", ["HudRailBrush"] = "#F203080D", ["HudPanelBrush"] = "#F0060F18", ["HudPanelStrongBrush"] = "#F00A1826",
            ["HudTagBrush"] = "#FF93C6D6", ["HudTextBrush"] = "#FFF3FAFF", ["HudTextDimBrush"] = "#FFC6DDE6",
            ["SolidBackgroundFillColorBaseBrush"] = "#FF04070B", ["SolidBackgroundFillColorSecondaryBrush"] = "#FF07101A",
            ["LayerFillColorDefaultBrush"] = "#B3070F17", ["CardBackgroundFillColorDefaultBrush"] = "#F0060F18", ["CardBackgroundFillColorSecondaryBrush"] = "#E00A1826",
            ["ControlFillColorDefaultBrush"] = "#FF0B1824", ["ControlFillColorSecondaryBrush"] = "#FF10202F", ["ControlFillColorTertiaryBrush"] = "#FF08131D",
            ["ControlFillColorInputActiveBrush"] = "#FF050C13", ["ControlStrongFillColorDefaultBrush"] = "#FF5E8C9C",
            ["TextFillColorPrimaryBrush"] = "#FFF3FAFF", ["TextFillColorSecondaryBrush"] = "#FFC6DDE6", ["TextFillColorTertiaryBrush"] = "#FF93C6D6",
            ["TextOnAccentFillColorPrimaryBrush"] = "#FF031016",
            ["SystemFillColorCautionBrush"] = "#FFFFB547", ["SystemFillColorCriticalBrush"] = "#FFFF4F6E", ["SystemFillColorSuccessBrush"] = "#FF39F5B0",
            ["SystemFillColorCautionBackgroundBrush"] = "#26FFB547", ["SystemFillColorCriticalBackgroundBrush"] = "#26FF4F6E", ["SystemFillColorSuccessBackgroundBrush"] = "#2639F5B0",
            ["AcrylicBackgroundFillColorDefaultBrush"] = "#F7071019", ["ContentDialogBackground"] = "#FF07111A", ["ContentDialogTopOverlay"] = "#FF0A1826",
            ["ToolTipBackground"] = "#FF07111A",
        },
        [("cyan", "#FF4FE3FF"), ("violet", "#FF9D7CFF"), ("green", "#FF39F5B0"), ("magenta", "#FFFF4FD8"), ("amber", "#FFFFB547")],
        ["#FF35E0F0", "#FFFFB547", "#FFFF5C8A", "#FF7CF09A", "#FFB48CFF", "#FF5AA9FF", "#FFFF8A4C", "#FFD8DEE4"]);

    /// <summary>Light and soft: pale blue-grey background, white panels, dark slate text, calm accent shades.</summary>
    public static readonly HudSkin Light = new(
        "light",
        Dark: false,
        new Dictionary<string, string>
        {
            ["HudWarnBrush"] = "#FFB86E00", ["HudWarnDimBrush"] = "#24B86E00", ["HudCritBrush"] = "#FFCC3350", ["HudGoodBrush"] = "#FF16936A",
            ["HudBackgroundBrush"] = "#FFEDF1F5", ["HudRailBrush"] = "#FFE2E8EE", ["HudPanelBrush"] = "#FAFFFFFF", ["HudPanelStrongBrush"] = "#FFF5F8FB",
            ["HudTagBrush"] = "#FF5E7385", ["HudTextBrush"] = "#FF1B2733", ["HudTextDimBrush"] = "#FF44566A",
            ["SolidBackgroundFillColorBaseBrush"] = "#FFEDF1F5", ["SolidBackgroundFillColorSecondaryBrush"] = "#FFE5EAF0",
            ["LayerFillColorDefaultBrush"] = "#B3FFFFFF", ["CardBackgroundFillColorDefaultBrush"] = "#FAFFFFFF", ["CardBackgroundFillColorSecondaryBrush"] = "#FFF5F8FB",
            ["ControlFillColorDefaultBrush"] = "#FFFFFFFF", ["ControlFillColorSecondaryBrush"] = "#FFF4F7FA", ["ControlFillColorTertiaryBrush"] = "#FFEEF2F6",
            ["ControlFillColorInputActiveBrush"] = "#FFFFFFFF", ["ControlStrongFillColorDefaultBrush"] = "#FF8596A7",
            ["TextFillColorPrimaryBrush"] = "#FF1B2733", ["TextFillColorSecondaryBrush"] = "#FF44566A", ["TextFillColorTertiaryBrush"] = "#FF5E7385",
            ["TextOnAccentFillColorPrimaryBrush"] = "#FFFFFFFF",
            ["SystemFillColorCautionBrush"] = "#FFB86E00", ["SystemFillColorCriticalBrush"] = "#FFCC3350", ["SystemFillColorSuccessBrush"] = "#FF16936A",
            ["SystemFillColorCautionBackgroundBrush"] = "#24B86E00", ["SystemFillColorCriticalBackgroundBrush"] = "#24CC3350", ["SystemFillColorSuccessBackgroundBrush"] = "#2416936A",
            ["AcrylicBackgroundFillColorDefaultBrush"] = "#FAFFFFFF", ["ContentDialogBackground"] = "#FFFFFFFF", ["ContentDialogTopOverlay"] = "#FFF5F8FB",
            ["ToolTipBackground"] = "#FFFFFFFF",
        },
        [("cyan", "#FF1288A8"), ("violet", "#FF6A52C9"), ("green", "#FF16936A"), ("magenta", "#FFB8388F"), ("amber", "#FFB86E00")],
        ["#FF1288A8", "#FFD07A00", "#FFD2386A", "#FF2E9E5B", "#FF7A5AD8", "#FF2F6FD0", "#FFD8602A", "#FF5E6E7E"]);

    /// <summary>
    /// Futuristic metal: mid-tone brushed silver (neither dark nor light), graphite text, panels lit like polished steel
    /// and an electric-blue accent.
    /// </summary>
    public static readonly HudSkin Metal = new(
        "metal",
        Dark: false,
        new Dictionary<string, string>
        {
            ["HudWarnBrush"] = "#FF9C5600", ["HudWarnDimBrush"] = "#2E9C5600", ["HudCritBrush"] = "#FFAD1233", ["HudGoodBrush"] = "#FF08744C",
            ["HudBackgroundBrush"] = "#FF8F979F", ["HudRailBrush"] = "#FFA3ABB3", ["HudPanelBrush"] = "#FFC3CAD1", ["HudPanelStrongBrush"] = "#FFCDD3D9",
            ["HudTagBrush"] = "#FF2B3743", ["HudTextBrush"] = "#FF0C1115", ["HudTextDimBrush"] = "#FF242E37",
            ["SolidBackgroundFillColorBaseBrush"] = "#FF8F979F", ["SolidBackgroundFillColorSecondaryBrush"] = "#FF9AA2AA",
            ["LayerFillColorDefaultBrush"] = "#B3C3CAD1", ["CardBackgroundFillColorDefaultBrush"] = "#FFC3CAD1", ["CardBackgroundFillColorSecondaryBrush"] = "#FFCDD3D9",
            ["ControlFillColorDefaultBrush"] = "#FFD7DCE1", ["ControlFillColorSecondaryBrush"] = "#FFE0E4E8", ["ControlFillColorTertiaryBrush"] = "#FFCBD1D7",
            ["ControlFillColorInputActiveBrush"] = "#FFE8EBEE", ["ControlStrongFillColorDefaultBrush"] = "#FF4E5964",
            ["TextFillColorPrimaryBrush"] = "#FF0C1115", ["TextFillColorSecondaryBrush"] = "#FF242E37", ["TextFillColorTertiaryBrush"] = "#FF2B3743",
            ["TextOnAccentFillColorPrimaryBrush"] = "#FFFFFFFF",
            ["SystemFillColorCautionBrush"] = "#FF9C5600", ["SystemFillColorCriticalBrush"] = "#FFAD1233", ["SystemFillColorSuccessBrush"] = "#FF08744C",
            ["SystemFillColorCautionBackgroundBrush"] = "#2E9C5600", ["SystemFillColorCriticalBackgroundBrush"] = "#2EAD1233", ["SystemFillColorSuccessBackgroundBrush"] = "#2E08744C",
            ["AcrylicBackgroundFillColorDefaultBrush"] = "#FAC3CAD1", ["ContentDialogBackground"] = "#FFC9CFD5", ["ContentDialogTopOverlay"] = "#FFD3D8DD",
            ["ToolTipBackground"] = "#FFD7DCE1",
        },
        [("cyan", "#FF005F86"), ("violet", "#FF4A2FB0"), ("green", "#FF086B47"), ("magenta", "#FF94166F"), ("amber", "#FF8A4B00")],
        ["#FF005F86", "#FF9C4E00", "#FFA8164A", "#FF0B6B3A", "#FF4F2DB8", "#FF1B4FA8", "#FFA33D10", "#FF1E262E"],
        new Dictionary<string, string[]>
        {
            // Brushed steel: light bands across a mid grey, so it reads as metal rather than flat grey.
            ["HudBackgroundBrush"] = ["#FF9AA2AA", "#FF838B93", "#FF959DA5", "#FF7B838B"],
            ["HudRailBrush"] = ["#FFB4BBC2", "#FF9199A1", "#FFA5ADB4"],
            ["HudPanelBrush"] = ["#FFD5DADF", "#FFB8C0C7", "#FFCAD0D6", "#FFB2BAC1"],
            ["HudPanelStrongBrush"] = ["#FFDDE1E5", "#FFC3CAD0", "#FFD4D9DE"],
        });

    public static IReadOnlyList<HudSkin> All { get; } = [Hud, Light, Metal];

    /// <summary>The skin applied at start (what the whole session shows).</summary>
    public static HudSkin Current { get; private set; } = Hud;

    public static HudSkin Of(string? id) => All.FirstOrDefault(s => s.Id == id) ?? Hud;

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
