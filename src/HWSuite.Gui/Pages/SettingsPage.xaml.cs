using System.Diagnostics;
using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HWSuite.Gui.Pages;

/// <summary>Interval, theme, tray and startup behavior, alert limits and CSV logging. Every change is saved immediately.</summary>
public sealed partial class SettingsPage : Page
{
    private static readonly string?[] Languages = [null, "pt-BR", "en"];
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["app.nav.settings"].ToUpper(s.Culture);
        GeneralTitle.Text = s["settings.general"];
        IntervalBox.Header = s["settings.interval"];
        IntervalBox.Minimum = AppSettings.MinInterval;
        IntervalBox.Maximum = AppSettings.MaxInterval;
        LanguageBox.Header = s["settings.language"];
        LanguageBox.Items.Add(s["settings.language.system"]);
        LanguageBox.Items.Add("Português (Brasil)");
        LanguageBox.Items.Add("English");
        LanguageHint.Text = s["settings.language.restart"];
        // Appearance: theme and mode (applied at the next start), accent color (applied at once).
        ThemeHeader.Text = s["settings.skin"];
        SkinHint.Text = s["settings.skin.hint"];
        ModeChoice.Header = s["settings.mode"];
        ModeChoice.Items.Add(s["mode.dark"]);
        ModeChoice.Items.Add(s["mode.light"]);
        ModeChoice.Items.Add(s["mode.system"]);
        AccentHeader.Text = s["settings.accent"];

        CloseToTraySwitch.Header = s["settings.closeToTray"];
        UpdatesSwitch.Header = s["settings.checkUpdates"];
        CheckUpdatesButton.Content = s["app.update.check"];
        AdminSwitch.Header = s["settings.admin"];
        AdminHint.Text = s["settings.admin.hint"];
        StartupSwitch.Header = s["settings.startWithWindows"];
        AlertsTitle.Text = s["settings.alerts"];
        AlertsSwitch.Header = s["settings.alertsEnabled"];
        AlertsHint.Text = s["settings.alertsHint"];
        RestoreButton.Content = s["settings.alertsRestore"];
        FilesTitle.Text = s["files.title"];
        PerClientCheck.Content = s["files.perClient"];
        KitTitle.Text = s["kit.title"];
        KitText.Text = s["kit.body"];
        KitButton.Content = s["kit.create"];
        RemoveDriverCheck.Content = s["kit.removeDriver"];
        ReportsLabel.Text = s["files.reports.label"];
        ReportsBrowseButton.Content = s["files.browse"];
        ReportsResetButton.Content = s["files.askNext"];
        LogoLabel.Text = s["files.logo.label"];
        LogoBrowseButton.Content = s["files.browse"];
        LogoNoneButton.Content = s["files.logo.without"];
        LogoResetButton.Content = s["files.askNext"];
        LogTitle.Text = s["settings.logging"];
        LogSwitch.Header = s["settings.logEnabled"];
        OpenFolderButton.Content = s["settings.openFolder"];
        ChangeLogFolderButton.Content = s["settings.logFolderChange"];
        DefaultLogFolderButton.Content = s["settings.logFolderDefault"];
        PsuTitle.Text = s["app.psu.title"];
        PsuSwitch.Header = s["app.psu.alerts"];
        PsuHint.Text = s["app.psu.hint"];
        PumpBox.Header = s["app.pump.header"];
        PumpMinBox.Header = s["app.pump.min"];

        Load();
    }

    private void Load()
    {
        _loading = true;
        var settings = App.Settings.Current;
        IntervalBox.Value = settings.IntervalSeconds;
        ModeChoice.SelectedIndex = App.Settings.Current.ThemeMode switch { "light" => 1, "system" => 2, "dark" => 0, _ => App.Mode switch { Hud.ThemeMode.Light => 1, Hud.ThemeMode.System => 2, _ => 0 } };
        BuildThemeCards();
        BuildAccentSwatches();
        LanguageBox.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, settings.Language));
        CloseToTraySwitch.IsOn = settings.CloseToTray;
        UpdatesSwitch.IsOn = settings.CheckForUpdates;
        AlertsSwitch.IsOn = settings.AlertsEnabled;
        LogSwitch.IsOn = settings.LogEnabled;
        LogFolderText.Text = App.Text.Format("settings.logFolder", App.Settings.LogFolder);
        ShowFiles();
        PsuSwitch.IsOn = settings.PsuAlertsEnabled;
        PumpMinBox.Value = settings.PumpMinRpm;
        LoadPumpChoices(settings.PumpSensorId);

        // The task is the source of truth (it may have been removed in Task Scheduler).
        var elevated = App.Monitor.Access.IsElevated;
        StartupSwitch.IsOn = StartupTask.IsRegistered();
        AdminSwitch.IsOn = settings.AlwaysRunAsAdmin;
        // A portable copy (a pen drive at a client's) never registers itself to start with Windows.
        var portable = Core.Settings.DataLocation.IsPortable;
        StartupSwitch.IsEnabled = elevated && !portable;
        StartupHint.Text = App.Text[portable ? "kit.noStartup" : "settings.startWithWindowsNeedsAdmin"];
        StartupHint.Visibility = elevated && !portable ? Visibility.Collapsed : Visibility.Visible;
        PerClientCheck.IsChecked = settings.ReportsPerClient;
        RemoveDriverCheck.IsChecked = settings.RemoveDriverOnExit;

        BuildRules();
        _loading = false;
    }

    private async void AdminSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Settings.Update(x => x with { AlwaysRunAsAdmin = AdminSwitch.IsOn });
        if (!AdminSwitch.IsOn || App.Monitor.Access.IsElevated)
        {
            return;
        }

        var t = App.Text;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = t["settings.admin.restartTitle"],
            Content = new TextBlock { Text = t["settings.admin.restartBody"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = t["settings.skin.restartNow"],
            CloseButtonText = t["settings.skin.restartLater"],
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && Services.Elevation.RelaunchElevated())
        {
            App.Quit();
        }
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        App.Settings.Update(s => s with { IntervalSeconds = (int)Math.Round(args.NewValue) });
    }

    private string ChosenTheme => App.Settings.Current.UiTheme ?? App.Theme.Id;

    /// <summary>One card per theme: a small drawing of its menu, panels and colors, its name and a line about it.</summary>
    private void BuildThemeCards()
    {
        ThemeCards.Children.Clear();
        var s = App.Text;
        var dark = App.Skin.Dark;
        foreach (var theme in Hud.HudThemes.All)
        {
            var palette = dark ? theme.Dark : theme.Light;
            Microsoft.UI.Xaml.Media.Brush B(string key) => new Microsoft.UI.Xaml.Media.SolidColorBrush(Hud.HudSkins.Parse(palette.Colors[key]));
            var accentHex = Hud.HudAccents.Palette.First(a => a.Id == theme.DefaultAccent);
            var accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Hud.HudSkins.Parse(dark ? accentHex.OnDark : accentHex.OnLight));
            var radius = theme.Shape == Hud.PanelShape.Rounded ? Math.Min(6, theme.Radius / 2) : 0;

            // Mini preview: menu (rail / wide / top), two panels and an accent bar.
            var preview = new Grid { Width = 176, Height = 96, Background = B("HudBackgroundBrush"), CornerRadius = new CornerRadius(4) };
            var body = new Grid { Margin = new Thickness(theme.Nav switch { Hud.NavMode.Rail => 22, Hud.NavMode.Wide => 48, _ => 6 }, theme.Nav == Hud.NavMode.Top ? 18 : 6, 6, 6), ColumnSpacing = 5, RowSpacing = 5 };
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition());
            for (var i = 0; i < 3; i++)
            {
                var panel = new Border
                {
                    Background = B("HudPanelBrush"),
                    BorderBrush = theme.Shape == Hud.PanelShape.Bracket || theme.Glow ? accent : B("HudTagBrush"),
                    BorderThickness = new Thickness(theme.Shape == Hud.PanelShape.Rounded && !dark ? 0 : 1),
                    CornerRadius = new CornerRadius(radius),
                };
                if (i == 0)
                {
                    panel.Child = new Border { Height = 4, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Bottom, Background = accent, CornerRadius = new CornerRadius(radius > 0 ? 2 : 0) };
                }

                Grid.SetRow(panel, i == 0 ? 0 : 1);
                Grid.SetColumn(panel, i == 2 ? 1 : 0);
                if (i == 0)
                {
                    Grid.SetColumnSpan(panel, 2);
                }

                body.Children.Add(panel);
            }

            preview.Children.Add(theme.Nav == Hud.NavMode.Top
                ? new Border { Height = 12, VerticalAlignment = VerticalAlignment.Top, Background = B("HudRailBrush"), Child = new Border { Width = 26, Height = 2, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = accent } }
                : new Border { Width = theme.Nav == Hud.NavMode.Wide ? 42 : 16, HorizontalAlignment = HorizontalAlignment.Left, Background = B("HudRailBrush"), Child = new Border { Height = 3, Margin = new Thickness(3, 10, 3, 0), VerticalAlignment = VerticalAlignment.Top, Background = accent } });
            preview.Children.Add(body);

            var card = new StackPanel { Spacing = 6, Width = 176 };
            card.Children.Add(preview);
            card.Children.Add(new TextBlock { Text = s[$"theme.{theme.Id}"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            card.Children.Add(new TextBlock { Text = s[$"theme.{theme.Id}.d"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Hud.HudUi.TextDim, Height = 34 });
            var selected = theme.Id == ChosenTheme;
            var button = new Button
            {
                Content = card,
                Padding = new Thickness(8),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? Hud.HudUi.Accent : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = theme.Id,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, s[$"theme.{theme.Id}"]);
            button.Click += async (_, _) => await ChooseThemeAsync(theme);
            ThemeCards.Children.Add(button);
        }
    }

    private async Task ChooseThemeAsync(Hud.HudTheme theme)
    {
        // A new theme starts with its own accent (readable on its colors); the user can change it after.
        App.Settings.Update(x => x with { UiTheme = theme.Id, Accent = theme.Id == ChosenTheme ? x.Accent : theme.DefaultAccent });
        BuildThemeCards();
        await OfferRestartAsync(App.Text[$"theme.{theme.Id}"]);
    }

    private async void ModeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeChoice.SelectedIndex < 0)
        {
            return;
        }

        var mode = ModeChoice.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" };
        App.Settings.Update(x => x with { ThemeMode = mode });
        await OfferRestartAsync(App.Text[$"mode.{(mode == "light" ? "light" : mode == "system" ? "system" : "dark")}"]);
    }

    /// <summary>Theme and mode take effect at the next start: offer to restart now (nothing to do if unchanged).</summary>
    private async Task OfferRestartAsync(string what)
    {
        var settings = App.Settings.Current;
        var theme = Hud.HudThemes.Of(settings.UiTheme);
        var mode = Hud.HudThemes.ModeOf(settings.ThemeMode, theme);
        if (theme.Id == App.Theme.Id && mode == App.Mode)
        {
            return;
        }

        var t = App.Text;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = t["settings.skin.restartTitle"],
            Content = new TextBlock { Text = t.Format("settings.skin.restartBody", what), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = t["settings.skin.restartNow"],
            CloseButtonText = t["settings.skin.restartLater"],
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Restart();
        }
    }

    /// <summary>Round swatches for the 14 accents (shades for this theme's background) and a custom color picker.</summary>
    private void BuildAccentSwatches()
    {
        AccentSwatches.Children.Clear();
        var s = App.Text;
        var current = App.Settings.Current.Accent ?? App.Theme.DefaultAccent;
        foreach (var (id, color) in Hud.HudAccents.All)
        {
            AccentSwatches.Children.Add(Swatch(id, s[$"accent.{id}"], color, current == id));
        }

        // Custom: a swatch showing the chosen color (or a rainbow ring) that opens a color picker.
        var custom = Hud.HudAccents.TryParse(App.Settings.Current.AccentCustom, out var c) ? c : Microsoft.UI.Colors.Gray;
        var picker = new ColorPicker { Color = custom, IsAlphaEnabled = false, IsMoreButtonVisible = true, ColorSpectrumShape = ColorSpectrumShape.Ring };
        var apply = new Button { Content = s["settings.accent.apply"], Style = Hud.HudUi.Style("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Right };
        var flyoutContent = new StackPanel { Spacing = 10 };
        flyoutContent.Children.Add(picker);
        flyoutContent.Children.Add(apply);
        var flyout = new Flyout { Content = flyoutContent };
        var customButton = Swatch("custom", s["settings.accent.custom"], custom, current == "custom", plus: true);
        FlyoutBase.SetAttachedFlyout(customButton, flyout);
        customButton.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(customButton);
        apply.Click += (_, _) =>
        {
            var hex = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
            App.Settings.Update(x => x with { Accent = "custom", AccentCustom = hex });
            Hud.HudAccents.Custom = hex;
            Hud.HudAccents.Apply("custom");
            flyout.Hide();
            BuildAccentSwatches();
        };
        AccentSwatches.Children.Add(customButton);
    }

    private Button Swatch(string id, string name, Windows.UI.Color color, bool selected, bool plus = false)
    {
        var dot = new Grid { Width = 28, Height = 28 };
        dot.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color) });
        if (plus)
        {
            dot.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) });
        }

        var button = new Button
        {
            Content = dot,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? Hud.HudUi.Text : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        if (!plus)
        {
            button.Click += (_, _) =>
            {
                App.Settings.Update(x => x with { Accent = id });
                Hud.HudAccents.Apply(id);
                BuildAccentSwatches();
            };
        }

        return button;
    }

    private void CloseToTraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.Settings.Update(s => s with { CloseToTray = CloseToTraySwitch.IsOn });
        }
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enable = StartupSwitch.IsOn;
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HWSuite.Gui.exe");
        StartupSwitch.IsEnabled = false;
        var error = await Task.Run(() => enable ? StartupTask.Register(exe) : StartupTask.Unregister());
        StartupSwitch.IsEnabled = true;

        if (error is not null)
        {
            StartupHint.Text = App.Text.Format("settings.startWithWindowsFailed", error);
            StartupHint.Visibility = Visibility.Visible;
            _loading = true;
            StartupSwitch.IsOn = !enable;
            _loading = false;
            return;
        }

        StartupHint.Visibility = Visibility.Collapsed;
        App.Settings.Update(s => s with { StartWithWindows = enable });
    }

    private void AlertsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.Settings.Update(s => s with { AlertsEnabled = AlertsSwitch.IsOn });
        }
    }

    private void LogSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.Settings.Update(s => s with { LogEnabled = LogSwitch.IsOn });
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = App.Settings.LogFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Update(s => s with { Alerts = null });
        BuildRules();
    }

    /// <summary>One row per rule: on/off, name, direction, editable limit with unit, remove.</summary>
    private void BuildRules()
    {
        RulesPanel.Children.Clear();
        var s = App.Text;
        foreach (var rule in App.Settings.Rules)
        {
            var kind = rule.Target.Kind ?? KindOf(rule);
            var unit = SensorFormat.Unit(kind);

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var enabled = new CheckBox { IsChecked = rule.Enabled, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            enabled.Click += (_, _) => Replace(rule.Id, r => r with { Enabled = enabled.IsChecked == true });

            var label = new TextBlock { Text = rule.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1);

            var direction = new TextBlock
            {
                Text = s[rule.Direction == AlertDirection.Above ? "settings.alertAbove" : "settings.alertBelow"],
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Grid.SetColumn(direction, 2);

            var limit = new NumberBox
            {
                Value = rule.Threshold,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                SmallChange = kind is SensorKind.Fan or SensorKind.Clock ? 50 : 1,
                PlaceholderText = unit,
            };
            if (unit.Length > 0)
            {
                ToolTipService.SetToolTip(limit, unit);
            }

            limit.ValueChanged += (_, args) =>
            {
                if (!double.IsNaN(args.NewValue))
                {
                    Replace(rule.Id, r => r with { Threshold = args.NewValue });
                }
            };
            Grid.SetColumn(limit, 3);

            var unitText = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, MinWidth = 28 };
            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
            ToolTipService.SetToolTip(remove, s["settings.alertRemove"]);
            remove.Click += (_, _) =>
            {
                App.Settings.UpdateRules(rules => rules.Where(r => r.Id != rule.Id));
                BuildRules();
            };
            var tail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { unitText, remove } };
            Grid.SetColumn(tail, 4);

            row.Children.Add(enabled);
            row.Children.Add(label);
            row.Children.Add(direction);
            row.Children.Add(limit);
            row.Children.Add(tail);
            RulesPanel.Children.Add(row);
        }
    }

    private static void Replace(string id, Func<AlertRule, AlertRule> change) =>
        App.Settings.UpdateRules(rules => rules.Select(r => r.Id == id ? change(r) : r));

    /// <summary>Unit for single-sensor rules, taken from the live reading of that sensor.</summary>
    private static SensorKind KindOf(AlertRule rule) =>
        App.Monitor.Latest?.Readings.FirstOrDefault(r => r.Id == rule.Target.SensorId)?.Kind ?? SensorKind.Other;

    private readonly List<string?> _pumpIds = [];

    /// <summary>"None" plus every fan reading the monitor has, with its current speed; the saved choice stays listed even if absent now.</summary>
    private void LoadPumpChoices(string? selected)
    {
        var s = App.Text;
        PumpBox.Items.Clear();
        _pumpIds.Clear();
        PumpBox.Items.Add(s["app.pump.none"]);
        _pumpIds.Add(null);
        var fans = App.Monitor.Latest?.Readings.Where(r => r.Kind == SensorKind.Fan).ToList() ?? [];
        foreach (var fan in fans)
        {
            var speed = fan.Value is { } rpm ? string.Create(s.Culture, $" · {rpm:N0} RPM") : "";
            PumpBox.Items.Add($"{fan.Name} · {fan.Hardware}{speed}");
            _pumpIds.Add(fan.Id);
        }

        if (selected is not null && !_pumpIds.Contains(selected))
        {
            PumpBox.Items.Add(selected);
            _pumpIds.Add(selected);
        }

        PumpBox.SelectedIndex = Math.Max(0, _pumpIds.IndexOf(selected));
        PumpHint.Text = fans.Count == 0 ? s["app.pump.hint"] + " " + s["app.pump.noSensors"] : s["app.pump.hint"];
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesText.Text = s["app.update.checking"];
        try
        {
            var result = await App.FindUpdateAsync();
            CheckUpdatesText.Text = result switch
            {
                null => s["app.update.failed"],
                { Newer: true } r => s.Format("app.update.title", r.Release.Version),
                _ => s.Format("app.update.latest", Core.BuildInfo.Current.Version),
            };
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && LanguageBox.SelectedIndex >= 0)
        {
            var language = Languages[LanguageBox.SelectedIndex];
            App.Settings.Update(s => s with { Language = language });
            LanguageHint.Visibility = Visibility.Visible; // texts are loaded at start
        }
    }

    private void UpdatesSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.Settings.Update(s => s with { CheckForUpdates = UpdatesSwitch.IsOn });
        }
    }

    private void PsuSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.Settings.Update(s => s with { PsuAlertsEnabled = PsuSwitch.IsOn });
        }
    }

    private void PumpBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && PumpBox.SelectedIndex >= 0 && PumpBox.SelectedIndex < _pumpIds.Count)
        {
            var id = _pumpIds[PumpBox.SelectedIndex];
            App.Settings.Update(s => s with { PumpSensorId = id });
        }
    }

    private void PumpMinBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading && !double.IsNaN(args.NewValue))
        {
            App.Settings.Update(s => s with { PumpMinRpm = (int)args.NewValue });
        }
    }

    // ---- Files: where reports go and which logo they carry --------------------------------------------------------

    private void ShowFiles()
    {
        var s = App.Text;
        var settings = App.Settings.Current;
        ReportsFolderText.Text = App.Settings.ReportsFolder is { Length: > 0 } folder ? folder : s["files.reports.notSet"];
        LogoFileText.Text = App.Settings.LogoFile switch
        {
            "" => s["files.logo.noneSaved"],
            { } file => file,
            null => s["files.logo.notSet"],
        };
    }

    private void ReportsBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var start = App.Settings.ReportsFolder ?? Services.OutputLocations.SuggestedReportsFolder;
        if (Services.ShellDialogs.PickFolder(App.Text["files.reports.pick"], start) is { } folder)
        {
            var stored = Core.Settings.DataLocation.StorePath(folder);
            App.Settings.Update(x => x with { ReportsFolder = stored });
            ShowFiles();
        }
    }

    private void ReportsResetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Update(x => x with { ReportsFolder = null });
        ShowFiles();
    }

    private void LogoBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var start = App.Settings.LogoFile is { Length: > 0 } current ? current : Core.Benchmark.BenchmarkFiles.Folder;
        if (Services.ShellDialogs.PickFile(s["files.logo.pick"], start, (s["files.logo.images"], "*.png;*.jpg;*.jpeg;*.svg")) is { } file)
        {
            var stored = Core.Settings.DataLocation.StorePath(file);
            App.Settings.Update(x => x with { LogoFile = stored });
            ShowFiles();
        }
    }

    private void LogoNoneButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Update(x => x with { LogoFile = "" });
        ShowFiles();
    }

    private void LogoResetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Update(x => x with { LogoFile = null });
        ShowFiles();
    }

    private void ChangeLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Services.ShellDialogs.PickFolder(App.Text["charts.logFolder"], App.Settings.LogFolder) is { } folder)
        {
            var stored = Core.Settings.DataLocation.StorePath(folder);
            App.Settings.Update(x => x with { LogFolder = stored });
            LogFolderText.Text = App.Text.Format("settings.logFolder", App.Settings.LogFolder);
        }
    }

    private void DefaultLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Update(x => x with { LogFolder = null });
        LogFolderText.Text = App.Text.Format("settings.logFolder", App.Settings.LogFolder);
    }

    // ---- Technician's pen drive ----------------------------------------------------------------------------------

    private void PerClientCheck_Click(object sender, RoutedEventArgs e)
    {
        var on = PerClientCheck.IsChecked == true;
        App.Settings.Update(x => x with { ReportsPerClient = on });
    }

    private void RemoveDriverCheck_Click(object sender, RoutedEventArgs e)
    {
        var on = RemoveDriverCheck.IsChecked == true;
        App.Settings.Update(x => x with { RemoveDriverOnExit = on });
    }

    private async void KitButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (Services.ShellDialogs.PickFolder(s["kit.pick"], null) is not { } target)
        {
            return;
        }

        KitButton.IsEnabled = false;
        KitProgress.Visibility = Visibility.Visible;
        KitResult.Text = s["kit.copying"];
        try
        {
            var settings = App.Settings.Current;
            var logo = App.Settings.LogoFile;
            var progress = new Progress<double>(p => KitProgress.Value = p);
            var folder = await Task.Run(() => Services.PortableKit.Create(target, settings, logo, progress));
            KitResult.Text = s.Format("kit.done", folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            KitResult.Text = s.Format("kit.failed", ex.Message);
        }
        finally
        {
            KitButton.IsEnabled = true;
            KitProgress.Visibility = Visibility.Collapsed;
        }
    }
}
