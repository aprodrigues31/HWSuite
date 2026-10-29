using System.Diagnostics;
using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        PageTitle.Text = s["app.nav.settings"];
        GeneralTitle.Text = s["settings.general"];
        IntervalBox.Header = s["settings.interval"];
        IntervalBox.Minimum = AppSettings.MinInterval;
        IntervalBox.Maximum = AppSettings.MaxInterval;
        LanguageBox.Header = s["settings.language"];
        LanguageBox.Items.Add(s["settings.language.system"]);
        LanguageBox.Items.Add("Português (Brasil)");
        LanguageBox.Items.Add("English");
        LanguageHint.Text = s["settings.language.restart"];
        // The HUD is always dark; what can be chosen is its accent color (applied at once).
        ThemeChoice.Header = s["settings.accent"];
        foreach (var (id, _) in Hud.HudAccents.All)
        {
            ThemeChoice.Items.Add(s[$"accent.{id}"]);
        }

        CloseToTraySwitch.Header = s["settings.closeToTray"];
        UpdatesSwitch.Header = s["settings.checkUpdates"];
        CheckUpdatesButton.Content = s["app.update.check"];
        StartupSwitch.Header = s["settings.startWithWindows"];
        AlertsTitle.Text = s["settings.alerts"];
        AlertsSwitch.Header = s["settings.alertsEnabled"];
        AlertsHint.Text = s["settings.alertsHint"];
        RestoreButton.Content = s["settings.alertsRestore"];
        LogTitle.Text = s["settings.logging"];
        LogSwitch.Header = s["settings.logEnabled"];
        OpenFolderButton.Content = s["settings.openFolder"];
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
        ThemeChoice.SelectedIndex = Math.Max(0, Hud.HudAccents.All.ToList().FindIndex(a => a.Id == settings.Accent));
        LanguageBox.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, settings.Language));
        CloseToTraySwitch.IsOn = settings.CloseToTray;
        UpdatesSwitch.IsOn = settings.CheckForUpdates;
        AlertsSwitch.IsOn = settings.AlertsEnabled;
        LogSwitch.IsOn = settings.LogEnabled;
        LogFolderText.Text = App.Text.Format("settings.logFolder", App.Settings.LogFolder);
        PsuSwitch.IsOn = settings.PsuAlertsEnabled;
        PumpMinBox.Value = settings.PumpMinRpm;
        LoadPumpChoices(settings.PumpSensorId);

        // The task is the source of truth (it may have been removed in Task Scheduler).
        var elevated = App.Monitor.Access.IsElevated;
        StartupSwitch.IsOn = StartupTask.IsRegistered();
        StartupSwitch.IsEnabled = elevated;
        StartupHint.Text = App.Text["settings.startWithWindowsNeedsAdmin"];
        StartupHint.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;

        BuildRules();
        _loading = false;
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        App.Settings.Update(s => s with { IntervalSeconds = (int)Math.Round(args.NewValue) });
    }

    private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeChoice.SelectedIndex >= 0)
        {
            var accent = Hud.HudAccents.All[ThemeChoice.SelectedIndex].Id;
            App.Settings.Update(s => s with { Accent = accent });
            Hud.HudAccents.Apply(accent);
        }
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
}
