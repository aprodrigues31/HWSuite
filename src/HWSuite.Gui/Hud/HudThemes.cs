using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HWSuite.Gui.Hud;

/// <summary>Where the pages are listed.</summary>
public enum NavMode
{
    /// <summary>Narrow column on the left: icon over a short label (HUD, Metal).</summary>
    Rail,

    /// <summary>Wide column on the left: icon and full name side by side (Windows 11, Minimal).</summary>
    Wide,

    /// <summary>A row of tabs at the top (Compact, Terminal, Cockpit, Hologram).</summary>
    Top,
}

/// <summary>How a panel is drawn.</summary>
public enum PanelShape
{
    Chamfer,
    Rounded,
    Square,

    /// <summary>Square with bright corner brackets (Hologram).</summary>
    Bracket,
}

/// <summary>Light, dark, or the one Windows uses.</summary>
public enum ThemeMode
{
    Dark,
    Light,
    System,
}

/// <summary>
/// One look of the whole interface: navigation, panel shape, fonts, glow and grid, and its two palettes (dark and
/// light). The palette colors are a <see cref="HudSkin"/>; the accent is chosen apart (<see cref="HudAccents"/>).
/// </summary>
public sealed record HudTheme(
    string Id,
    NavMode Nav,
    PanelShape Shape,
    double Radius,
    bool Glow,
    bool Grid,
    string BodyFont,
    string DisplayFont,
    string MonoFont,
    string BoldFont,
    double ControlRadius,
    string DefaultAccent,
    HudSkin Dark,
    HudSkin Light,
    ThemeMode DefaultMode = ThemeMode.Dark);

public static class HudThemes
{
    private const string Barlow = "ms-appx:///Assets/Fonts/Barlow-Medium.ttf#Barlow Medium, ms-appx:///Assets/Fonts/Barlow-Medium.ttf#Barlow";
    private const string Chakra = "ms-appx:///Assets/Fonts/ChakraPetch-SemiBold.ttf#Chakra Petch SemiBold, ms-appx:///Assets/Fonts/ChakraPetch-SemiBold.ttf#Chakra Petch";
    private const string ChakraBold = "ms-appx:///Assets/Fonts/ChakraPetch-Bold.ttf#Chakra Petch";
    private const string JetBrains = "ms-appx:///Assets/Fonts/JetBrainsMono-Medium.ttf#JetBrains Mono Medium, ms-appx:///Assets/Fonts/JetBrainsMono-Medium.ttf#JetBrains Mono";
    private const string JetBrainsBold = "ms-appx:///Assets/Fonts/JetBrainsMono-Bold.ttf#JetBrains Mono";
    private const string Segoe = "Segoe UI Variable Text, Segoe UI";
    private const string SegoeDisplay = "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI";
    private const string Cascadia = "Cascadia Mono, Consolas";
    private const string Bahnschrift = "Bahnschrift, Segoe UI";
    private const string BahnschriftCondensed = "Bahnschrift SemiBold SemiConden, Bahnschrift, Segoe UI";

    private static readonly string[] DarkCharts = ["#FF35E0F0", "#FFFFB547", "#FFFF5C8A", "#FF7CF09A", "#FFB48CFF", "#FF5AA9FF", "#FFFF8A4C", "#FFD8DEE4"];
    private static readonly string[] LightCharts = ["#FF1288A8", "#FFD07A00", "#FFD2386A", "#FF2E9E5B", "#FF7A5AD8", "#FF2F6FD0", "#FFD8602A", "#FF5E6E7E"];

    /// <summary>
    /// A palette from a few base colors; the WinUI control colors are derived from them, so every standard control
    /// (buttons, boxes, dialogs) matches the theme.
    /// </summary>
    private static HudSkin Palette(string id, bool dark, string bg, string rail, string panel, string strong, string text, string dim, string tag,
        IReadOnlyDictionary<string, string[]>? gradients = null, string? warn = null, string? crit = null, string? good = null)
    {
        warn ??= dark ? "#FFFFB547" : "#FFB86E00";
        crit ??= dark ? "#FFFF4F6E" : "#FFCC3350";
        good ??= dark ? "#FF39F5B0" : "#FF16936A";
        string Opaque(string hex) => "#FF" + hex[3..];
        string Alpha(string hex, string a) => "#" + a + hex[3..];
        var input = dark ? Opaque(Shade(bg, 0.85)) : "#FFFFFFFF";
        var colors = new Dictionary<string, string>
        {
            ["HudWarnBrush"] = warn, ["HudWarnDimBrush"] = Alpha(warn, "2E"), ["HudCritBrush"] = crit, ["HudGoodBrush"] = good,
            ["HudBackgroundBrush"] = bg, ["HudRailBrush"] = rail, ["HudPanelBrush"] = panel, ["HudPanelStrongBrush"] = strong,
            ["HudTagBrush"] = tag, ["HudTextBrush"] = text, ["HudTextDimBrush"] = dim,
            ["SolidBackgroundFillColorBaseBrush"] = Opaque(bg), ["SolidBackgroundFillColorSecondaryBrush"] = Opaque(rail),
            ["LayerFillColorDefaultBrush"] = Alpha(panel, "B3"), ["CardBackgroundFillColorDefaultBrush"] = panel, ["CardBackgroundFillColorSecondaryBrush"] = strong,
            ["ControlFillColorDefaultBrush"] = dark ? Opaque(Shade(strong, 1.15)) : "#FFFFFFFF",
            ["ControlFillColorSecondaryBrush"] = dark ? Opaque(Shade(strong, 1.3)) : Opaque(Shade(strong, 0.97)),
            ["ControlFillColorTertiaryBrush"] = dark ? Opaque(Shade(strong, 0.9)) : Opaque(Shade(strong, 0.94)),
            ["ControlFillColorInputActiveBrush"] = input, ["ControlStrongFillColorDefaultBrush"] = tag,
            ["TextFillColorPrimaryBrush"] = text, ["TextFillColorSecondaryBrush"] = dim, ["TextFillColorTertiaryBrush"] = tag,
            ["TextOnAccentFillColorPrimaryBrush"] = dark ? "#FF031016" : "#FFFFFFFF",
            ["SystemFillColorCautionBrush"] = warn, ["SystemFillColorCriticalBrush"] = crit, ["SystemFillColorSuccessBrush"] = good,
            ["SystemFillColorCautionBackgroundBrush"] = Alpha(warn, "26"), ["SystemFillColorCriticalBackgroundBrush"] = Alpha(crit, "26"), ["SystemFillColorSuccessBackgroundBrush"] = Alpha(good, "26"),
            ["AcrylicBackgroundFillColorDefaultBrush"] = Alpha(strong, "F7"), ["ContentDialogBackground"] = Opaque(strong), ["ContentDialogTopOverlay"] = Opaque(panel),
            ["ToolTipBackground"] = Opaque(strong),
        };
        return new HudSkin(id, dark, colors, [], dark ? DarkCharts : LightCharts, gradients);
    }

    /// <summary>Multiplies the RGB of "#AARRGGBB" (above 1 lightens toward white).</summary>
    private static string Shade(string hex, double factor)
    {
        var c = HudSkins.Parse(hex);
        byte F(byte v) => (byte)Math.Clamp(factor >= 1 ? v + ((255 - v) * (factor - 1)) : v * factor, 0, 255);
        return $"#{c.A:X2}{F(c.R):X2}{F(c.G):X2}{F(c.B):X2}";
    }

    public static readonly HudTheme Hud = new("hud", NavMode.Rail, PanelShape.Chamfer, 14, Glow: true, Grid: true, Barlow, Chakra, JetBrains, ChakraBold, 2, "cyan",
        Palette("hud-dark", true, "#FF04070B", "#F203080D", "#F0060F18", "#F00A1826", "#FFF3FAFF", "#FFC6DDE6", "#FF93C6D6"),
        Palette("hud-light", false, "#FFEDF1F5", "#FFE2E8EE", "#FAFFFFFF", "#FFF5F8FB", "#FF1B2733", "#FF44566A", "#FF5E7385"));

    public static readonly HudTheme Fluent = new("fluent", NavMode.Wide, PanelShape.Rounded, 8, Glow: false, Grid: false, Segoe, SegoeDisplay, Cascadia, SegoeDisplay, 4, "blue",
        Palette("fluent-dark", true, "#FF202020", "#FF1C1C1C", "#FF2B2B2B", "#FF323232", "#FFFFFFFF", "#FFC5C5C5", "#FF9D9D9D"),
        Palette("fluent-light", false, "#FFF3F3F3", "#FFEBEBEB", "#FFFBFBFB", "#FFFFFFFF", "#FF1A1A1A", "#FF5F5F5F", "#FF707070"));

    public static readonly HudTheme Compact = new("compact", NavMode.Top, PanelShape.Square, 0, Glow: false, Grid: false, Segoe, SegoeDisplay, Cascadia, SegoeDisplay, 0, "blue",
        Palette("compact-dark", true, "#FF161616", "#FF262626", "#FF1F1F1F", "#FF262626", "#FFF4F4F4", "#FFC6C6C6", "#FF8D8D8D"),
        Palette("compact-light", false, "#FFF2F4F8", "#FFE0E0E0", "#FFFFFFFF", "#FFF4F4F4", "#FF161616", "#FF393939", "#FF6F6F6F"),
        ThemeMode.Light);

    public static readonly HudTheme Metal = new("metal", NavMode.Rail, PanelShape.Rounded, 14, Glow: false, Grid: false, Bahnschrift, BahnschriftCondensed, JetBrains, BahnschriftCondensed, 6, "blue",
        Palette("metal-dark", true, "#FF2B3036", "#FF353B42", "#FF3D444C", "#FF47505A", "#FFEEF2F5", "#FFC4CCD3", "#FF9AA6B1",
            new Dictionary<string, string[]>
            {
                ["HudBackgroundBrush"] = ["#FF323840", "#FF252a30", "#FF2f353c", "#FF22272c"],
                ["HudRailBrush"] = ["#FF3d444c", "#FF2c3238", "#FF363d44"],
                ["HudPanelBrush"] = ["#FF4a525b", "#FF3a4148", "#FF454d55", "#FF383f46"],
                ["HudPanelStrongBrush"] = ["#FF525b64", "#FF414950", "#FF4b535b"],
            }),
        Palette("metal-light", false, "#FF8F979F", "#FFA3ABB3", "#FFC3CAD1", "#FFCDD3D9", "#FF0C1115", "#FF242E37", "#FF2B3743",
            new Dictionary<string, string[]>
            {
                ["HudBackgroundBrush"] = ["#FF9AA2AA", "#FF838B93", "#FF959DA5", "#FF7B838B"],
                ["HudRailBrush"] = ["#FFB4BBC2", "#FF9199A1", "#FFA5ADB4"],
                ["HudPanelBrush"] = ["#FFD5DADF", "#FFB8C0C7", "#FFCAD0D6", "#FFB2BAC1"],
                ["HudPanelStrongBrush"] = ["#FFDDE1E5", "#FFC3CAD0", "#FFD4D9DE"],
            }, "#FF9C5600", "#FFAD1233", "#FF08744C"),
        ThemeMode.Light);

    public static readonly HudTheme Terminal = new("terminal", NavMode.Top, PanelShape.Square, 0, Glow: true, Grid: false, JetBrains, JetBrainsBold, JetBrains, JetBrainsBold, 0, "green",
        Palette("terminal-dark", true, "#FF050A06", "#FF07110A", "#F007130B", "#F00B1A0F", "#FF5CFF85", "#FF33E066", "#FF22A84A", null, "#FFFFB000", "#FFFF5A4F", "#FF5CFF85"),
        Palette("terminal-light", false, "#FFF4F1E6", "#FFEBE6D3", "#FFFBF9F1", "#FFFFFFFF", "#FF1F3A26", "#FF335C3F", "#FF5B7A62"));

    public static readonly HudTheme Cockpit = new("cockpit", NavMode.Top, PanelShape.Rounded, 10, Glow: false, Grid: false, BahnschriftCondensed, BahnschriftCondensed, JetBrains, BahnschriftCondensed, 6, "orange",
        Palette("cockpit-dark", true, "#FF111315", "#FF16181B", "#FF1B1E21", "#FF222529", "#FFF2F2F2", "#FFC9CCD1", "#FFA7AAB0", null, "#FFFFD400", "#FFE10600", "#FF00D2BE"),
        Palette("cockpit-light", false, "#FFE9EAEC", "#FFDCDEE1", "#FFF7F7F8", "#FFFFFFFF", "#FF16181B", "#FF3C4046", "#FF6A6F76"));

    public static readonly HudTheme Minimal = new("minimal", NavMode.Wide, PanelShape.Rounded, 20, Glow: false, Grid: false, Segoe, SegoeDisplay, Cascadia, SegoeDisplay, 8, "blue",
        Palette("minimal-dark", true, "#FF1C1C1E", "#FF1C1C1E", "#FF2C2C2E", "#FF3A3A3C", "#FFF5F5F7", "#FFD1D1D6", "#FF98989D"),
        Palette("minimal-light", false, "#FFF6F6F3", "#FFF6F6F3", "#FFFFFFFF", "#FFFFFFFF", "#FF1D1D1F", "#FF3A3A3C", "#FF6E6E73"),
        ThemeMode.Light);

    public static readonly HudTheme Hologram = new("hologram", NavMode.Top, PanelShape.Bracket, 0, Glow: true, Grid: true, Bahnschrift, Chakra, JetBrains, ChakraBold, 2, "cyan",
        Palette("hologram-dark", true, "#FF020A16", "#E604162B", "#B306203C", "#CC08284A", "#FFD9F4FF", "#FFA8DCF2", "#FF8FD6F5",
            new Dictionary<string, string[]> { ["HudBackgroundBrush"] = ["#FF0B3A63", "#FF061A33", "#FF020A16", "#FF041226"] }),
        Palette("hologram-light", false, "#FFE8F4FB", "#FFD8ECF7", "#FFF5FBFE", "#FFFFFFFF", "#FF06203C", "#FF1D4566", "#FF3F6A8A"));

    public static IReadOnlyList<HudTheme> All { get; } = [Hud, Fluent, Compact, Metal, Terminal, Cockpit, Minimal, Hologram];

    /// <summary>The theme of this session (a change in Settings applies at the next start).</summary>
    public static HudTheme Current { get; private set; } = Hud;

    public static HudTheme Of(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Hud;

    /// <summary>"dark" / "light" / "system" (null: the theme's own default).</summary>
    public static ThemeMode ModeOf(string? mode, HudTheme theme) => mode switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        "system" => ThemeMode.System,
        _ => theme.DefaultMode,
    };

    /// <summary>Whether Windows uses dark apps right now.</summary>
    public static bool WindowsIsDark()
    {
        try
        {
            var background = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R + background.G + background.B < 384;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return true;
        }
    }

    /// <summary>The palette for a theme and mode.</summary>
    public static HudSkin Resolve(HudTheme theme, ThemeMode mode) => mode switch
    {
        ThemeMode.Light => theme.Light,
        ThemeMode.Dark => theme.Dark,
        _ => WindowsIsDark() ? theme.Dark : theme.Light,
    };

    /// <summary>
    /// Applies the theme's fonts and corner radii (before any window exists: XAML reads them when pages load);
    /// colors are applied by <see cref="HudSkins.Apply"/>.
    /// </summary>
    public static void Apply(HudTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Current = theme;
        var r = Application.Current.Resources;
        r["HudBodyFont"] = new FontFamily(theme.BodyFont);
        r["HudBodyRegularFont"] = new FontFamily(theme.BodyFont);
        r["HudBodyMediumFont"] = new FontFamily(theme.BodyFont);
        r["ContentControlThemeFontFamily"] = new FontFamily(theme.BodyFont);
        r["HudDisplayFont"] = new FontFamily(theme.DisplayFont);
        r["HudBoldFont"] = new FontFamily(theme.BoldFont);
        r["HudMonoFont"] = new FontFamily(theme.MonoFont);
        r["HudMonoMediumFont"] = new FontFamily(theme.MonoFont);
        r["HudMonoStrongFont"] = new FontFamily(theme.Id == "terminal" ? theme.BoldFont : theme.MonoFont);
        r["ControlCornerRadius"] = new CornerRadius(theme.ControlRadius);
        r["OverlayCornerRadius"] = new CornerRadius(Math.Max(theme.ControlRadius, theme.Shape == PanelShape.Rounded ? 8 : 0));
    }
}

/// <summary>An accent color with its shade for dark backgrounds and for light ones.</summary>
public sealed record HudAccent(string Id, string OnDark, string OnLight);
