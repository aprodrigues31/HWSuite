using HWSuite.Core.Maintenance;
using HWSuite.Gui.Hud;
using HWSuite.Providers.Windows;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Programs that start with Windows: one switch each to turn them on or off (the Task Manager way, nothing is
/// deleted), with publisher, where the entry lives and the command line, and a warning for leftovers whose file
/// is gone.
/// </summary>
public sealed partial class StartupPage : Page
{
    private IReadOnlyList<StartupEntry> _entries = [];
    private bool _admin;

    public StartupPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = ("// " + s["rail.startup"]).ToUpper(c);
        PageTitle.Text = s["startup.title"].ToUpper(c);
        Subtitle.Text = s["startup.subtitle"];
        RefreshButton.Content = s["startup.refresh"];
        SearchBox.PlaceholderText = s["startup.search"];
        EmptyText.Text = s["startup.empty"];
        NoteText.Text = s["startup.note"];
        AdminBar.Message = s["startup.needsAdmin"];
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Load();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Render();

    private void Load()
    {
        ErrorBar.IsOpen = false;
        if (App.IsDemo || !OperatingSystem.IsWindows())
        {
            _entries = StartupRules.Sort(Demo());
            _admin = true;
        }
        else
        {
            _entries = WindowsStartup.List();
            _admin = AccessStatus.Detect().IsElevated;
        }

        AdminBar.IsOpen = !_admin && _entries.Any(x => x.NeedsAdmin);
        Render();
    }

    private void Render()
    {
        var s = App.Text;
        var filter = SearchBox.Text.Trim();
        var shown = _entries.Where(x => filter.Length == 0
            || x.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || x.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || (x.Publisher?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || x.Command.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        CountText.Text = s.Format("startup.count", _entries.Count(x => x.Enabled), _entries.Count);
        Rows.Children.Clear();
        foreach (var entry in shown)
        {
            Rows.Children.Add(Row(entry));
        }

        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid Row(StartupEntry entry)
    {
        var s = App.Text;
        var grid = new Grid
        {
            ColumnSpacing = 18,
            Padding = new Thickness(0, 10, 0, 10),
            BorderBrush = HudUi.Brush("HudAccentFaintBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Opacity = entry.Enabled ? 1 : 0.62,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

        var toggle = new ToggleSwitch
        {
            IsOn = entry.Enabled,
            OnContent = s["startup.on"],
            OffContent = s["startup.off"],
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = _admin || !entry.NeedsAdmin,
        };
        toggle.Toggled += (_, _) => Change(entry, toggle);
        grid.Children.Add(toggle);

        var name = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = entry.DisplayName, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        name.Children.Add(new TextBlock { Text = entry.Publisher ?? s["startup.unknownPublisher"], FontSize = 12.5, Foreground = HudUi.TextDim, TextTrimming = TextTrimming.CharacterEllipsis });
        if (entry.FileMissing)
        {
            name.Children.Add(new TextBlock { Text = "⚠ " + s["startup.missing"], FontSize = 12, Foreground = HudUi.Warn, TextWrapping = TextWrapping.Wrap });
        }

        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var where = new TextBlock { Text = s[$"startup.source.{entry.Source}"], FontSize = 12.5, Foreground = HudUi.TextDim, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(where, 2);
        grid.Children.Add(where);

        var command = new TextBlock { Text = entry.Command, FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.TextDim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(command, entry.Command);
        Grid.SetColumn(command, 3);
        grid.Children.Add(command);
        return grid;
    }

    private void Change(StartupEntry entry, ToggleSwitch toggle)
    {
        if (toggle.IsOn == entry.Enabled)
        {
            return;
        }

        try
        {
            if (!App.IsDemo && OperatingSystem.IsWindows())
            {
                WindowsStartup.SetEnabled(entry, toggle.IsOn);
            }

            _entries = [.. _entries.Select(x => x == entry ? x with { Enabled = toggle.IsOn } : x)];
            CountText.Text = App.Text.Format("startup.count", _entries.Count(x => x.Enabled), _entries.Count);
            if (toggle.Parent is Grid row)
            {
                row.Opacity = toggle.IsOn ? 1 : 0.62;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            toggle.IsOn = entry.Enabled;
            ErrorBar.Message = App.Text.Format("startup.failed", entry.DisplayName, ex.Message);
            ErrorBar.IsOpen = true;
        }
    }

    private static IEnumerable<StartupEntry> Demo() =>
    [
        new(StartupSource.UserRun, "OneDrive", "\"C:\\Users\\demo\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", null, "Microsoft Corporation", "Microsoft OneDrive", true, false),
        new(StartupSource.UserRun, "Discord", "C:\\Users\\demo\\AppData\\Local\\Discord\\Update.exe --processStart Discord.exe", null, "Discord Inc.", "Discord", true, false),
        new(StartupSource.UserRun, "Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", null, "Valve Corporation", "Steam", false, false),
        new(StartupSource.MachineRun, "SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe", null, "Microsoft Corporation", "Windows Security notification icon", true, false),
        new(StartupSource.MachineRun, "AMD Adrenalin", "\"C:\\Program Files\\AMD\\CNext\\CNext\\RadeonSoftware.exe\" -silentmode", null, "Advanced Micro Devices, Inc.", "AMD Software: Adrenalin Edition", true, false),
        new(StartupSource.MachineRun32, "OldUpdater", "\"C:\\Program Files (x86)\\OldApp\\updater.exe\"", null, null, null, true, true),
        new(StartupSource.UserFolder, "Spotify.lnk", "C:\\Users\\demo\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\Spotify.lnk", null, "Spotify AB", "Spotify", false, false),
    ];
}
