using HWSuite.Core;
using HWSuite.Core.Settings;
using HWSuite.Gui.Pages;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace HWSuite.Gui;

public sealed partial class MainWindow : Window
{
    private bool _installing;
    private bool _allowClose;
    private bool _toldAboutTray;
    private volatile bool _isShown;

    public MainWindow()
    {
        InitializeComponent();
        var s = App.Text;
        Title = $"HWSuite {BuildInfo.Current.Display}";
        AppWindow.Resize(new SizeInt32(1180, 820));
        if (File.Exists(App.IconPath))
        {
            AppWindow.SetIcon(App.IconPath);
        }

        AppWindow.Closing += AppWindow_Closing;
        Activated += (_, _) => _isShown = AppWindow.IsVisible;

        NavSensors.Content = s["app.nav.sensors"];
        NavInventory.Content = s["app.nav.inventory"];
        NavBench.Content = s["app.nav.bench"];
        NavSettings.Content = s["app.nav.settings"];
        NavAbout.Content = s["app.nav.about"];
        Nav.SelectedItem = NavSensors;

        ApplyTheme(App.Settings.Current.Theme);
        App.Settings.Changed += (_, settings) => ApplyTheme(settings.Theme);
        UpdateAccessBar(App.Monitor.Access);
    }

    /// <summary>
    /// True while the window is on screen; pages skip UI work while it sits in the tray. Kept in a
    /// field because it is read from the sensor thread, and AppWindow may only be used on the UI thread.
    /// </summary>
    public bool IsShown => _isShown;

    /// <summary>Lets the next Close() really close (exit from the tray menu, elevation relaunch).</summary>
    public void AllowClose() => _allowClose = true;

    public void ShowFromTray()
    {
        AppWindow.Show();
        _isShown = true;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !App.Settings.Current.CloseToTray || App.Tray is not { IsAdded: true } tray)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
        _isShown = false;
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            tray.Notify("HWSuite", App.Text["app.tray.stillRunning"], warning: false);
        }
    }

    private void ApplyTheme(AppTheme theme) => Root.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
        {
            "inventory" => typeof(InventoryPage),
            "bench" => typeof(BenchmarkPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(SensorsPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    private Core.Updates.ReleaseInfo? _update;
    private bool _updating;

    /// <summary>Installed by the setup (per-user folder) rather than run from an extracted zip.</summary>
    private static bool IsInstalled => AppContext.BaseDirectory.TrimEnd('\\').EndsWith(@"Programs\HWSuite", StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>
    /// "HWSuite 0.15.0 is available". Installed copies get "Update now" (verified download, silent install,
    /// restart); portable copies get the download link, so a second installation is never created unasked.
    /// </summary>
    public void ShowUpdate(Core.Updates.ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var s = App.Text;
        _update = release;
        UpdateBar.Title = s.Format("app.update.title", release.Version);
        UpdateBar.Message = s.Format("app.update.message", Core.BuildInfo.Current.Version);
        UpdateButton.Content = s[CanUpdateInPlace(release) ? "app.update.now" : release.Installer is null ? "app.update.page" : "app.update.download"];
        UpdateButton.IsEnabled = true;
        UpdateBar.IsOpen = true;
    }

    private static bool CanUpdateInPlace(Core.Updates.ReleaseInfo release) =>
        IsInstalled && release.Installer is not null && release.InstallerSha256 is not null;

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } release || _updating)
        {
            return;
        }

        if (!CanUpdateInPlace(release))
        {
            var link = release.Installer ?? release.Page;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return;
        }

        var s = App.Text;
        _updating = true;
        UpdateButton.IsEnabled = false;
        var progress = new Progress<double>(p => UpdateBar.Message = s.Format("app.update.downloading", (p * 100).ToString("0", s.Culture)));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var folder = Path.Combine(Path.GetTempPath(), "HWSuite-update");
        var setup = await Core.Updates.UpdateChecker.DownloadInstallerAsync(http, release, folder, progress);
        if (setup is null)
        {
            UpdateBar.Message = s["app.update.downloadFailed"];
            UpdateButton.Content = s["app.update.download"];
            UpdateButton.IsEnabled = true;
            _updating = false;
            _update = release with { InstallerSha256 = null }; // the next click opens the download in the browser
            return;
        }

        // Silent install over this copy; the setup starts the new version when it finishes (/RELAUNCH=1).
        UpdateBar.Message = s["app.update.installing"];
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1") { UseShellExecute = true })?.Dispose();
        App.Quit();
    }

    /// <summary>Explains what limits the sensors and offers the one action that fixes it.</summary>
    private void UpdateAccessBar(AccessStatus access)
    {
        var s = App.Text;
        if (!access.IsElevated)
        {
            AccessBar.Title = s["app.notElevatedTitle"];
            AccessBar.Message = s["app.notElevated"];
            AccessButton.Content = s["app.reopenAdmin"];
            AccessButton.Visibility = Visibility.Visible;
            AccessBar.Severity = InfoBarSeverity.Warning;
            AccessBar.IsOpen = true;
        }
        else if (!access.PawnIoInstalled)
        {
            AccessBar.Title = s["app.noDriverTitle"];
            AccessBar.Message = s["app.noDriver"];
            AccessButton.Content = s["app.installDriver"];
            AccessButton.Visibility = Visibility.Visible;
            AccessBar.Severity = InfoBarSeverity.Warning;
            AccessBar.IsOpen = true;
        }
        else
        {
            AccessBar.IsOpen = false;
        }
    }

    private async void AccessButton_Click(object sender, RoutedEventArgs e)
    {
        var access = App.Monitor.Access;
        if (!access.IsElevated)
        {
            if (Elevation.RelaunchElevated())
            {
                App.Quit(); // the elevated copy takes over
            }

            return;
        }

        if (access.PawnIoInstalled || _installing)
        {
            return;
        }

        _installing = true;
        AccessButton.Visibility = Visibility.Collapsed;
        AccessBar.Severity = InfoBarSeverity.Informational;
        AccessBar.Message = App.Text["app.installingDriver"];

        var s = App.Text;
        var result = await Task.Run(() => PawnIoInstaller.Install((step, detail) =>
            DispatcherQueue.TryEnqueue(() => AccessBar.Message = s.Format($"driver.step.{step}", detail ?? "—"))));

        _installing = false;
        AccessBar.Message = s.Format($"driver.outcome.{result.Outcome}", result.Detail ?? "");
        if (result.Succeeded)
        {
            AccessBar.Severity = InfoBarSeverity.Success;
            App.Monitor.Start(); // reopen the hardware so CPU and motherboard sensors appear
            AccessBar.Message = s["app.driverInstalled"];
            await Task.Delay(TimeSpan.FromSeconds(3));
            UpdateAccessBar(App.Monitor.Access);
        }
        else
        {
            AccessBar.Severity = InfoBarSeverity.Error;
            AccessButton.Visibility = Visibility.Visible;
        }
    }
}
