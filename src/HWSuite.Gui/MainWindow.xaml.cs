using HWSuite.Core;
using HWSuite.Gui.Pages;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI;
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

    private readonly Dictionary<string, RadioButton> _rail = new(StringComparer.Ordinal);

    public MainWindow()
    {
        InitializeComponent();
        var s = App.Text;
        Title = $"HWSuite {BuildInfo.Current.Display}";
        AppWindow.Resize(new SizeInt32(1440, 900));
        if (File.Exists(App.IconPath))
        {
            AppWindow.SetIcon(App.IconPath);
        }

        // The HUD title bar: content extends under it; caption buttons keep their place, recolored.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0xE8, 0xF7, 0xFF);
        bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0x6F, 0xA9, 0xBC);
        bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x30, 0x4F, 0xE3, 0xFF);
        bar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0xE8, 0xF7, 0xFF);
        bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x50, 0x4F, 0xE3, 0xFF);
        TitleVersion.Text = $"// {BuildInfo.Current.Display}".ToUpper(s.Culture);
        TitleMode.Text = App.IsDemo ? "// DEMO" : "";

        AppWindow.Closing += AppWindow_Closing;
        Activated += (_, _) => _isShown = AppWindow.IsVisible;

        RailTop.Children.Add(RailItem("dashboard", "\uF246", s["rail.dashboard"], s["rail.dashboard.tip"]));
        RailTop.Children.Add(RailItem("inventory", "\uE950", s["rail.hardware"], s["app.nav.inventory"]));
        RailTop.Children.Add(RailItem("sensors", "\uE9D9", s["rail.sensors"], s["app.nav.sensors"]));
        RailTop.Children.Add(RailItem("bench", "\uEC4A", s["rail.bench"], s["app.nav.bench"]));
        RailTop.Children.Add(RailItem("network", "\uE701", s["rail.network"], s["net.title"]));
        RailBottom.Children.Add(RailItem("settings", "\uE713", s["rail.settings"], s["app.nav.settings"]));
        RailBottom.Children.Add(RailItem("about", "\uE946", s["rail.about"], s["app.nav.about"]));
        BenchRunTab.Text = s["app.nav.benchRun"];
        BenchResultsTab.Text = s["app.nav.benchResults"];
        Navigate("dashboard");

        UpdateAccessBar(App.Monitor.Access);
    }

    /// <summary>Opens a page by its tag ("dashboard", "sensors", "inventory", "bench", "benchresults", "network", "settings", "about").</summary>
    public void Navigate(string tag)
    {
        var railTag = tag == "benchresults" ? "bench" : tag;
        if (_rail.TryGetValue(railTag, out var item) && item.IsChecked != true)
        {
            _navigating = tag;
            item.IsChecked = true; // raises Checked, which shows the page
            _navigating = null;
        }

        Show(tag);
    }

    private string? _navigating;

    private RadioButton RailItem(string tag, string glyph, string label, string tooltip)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = label.ToUpper(App.Text.Culture),
            FontFamily = Hud.HudUi.Font("HudMonoFont"),
            FontSize = 10.5,
            CharacterSpacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 82,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var item = new RadioButton { Content = content, Tag = tag, Style = Hud.HudUi.Style("HudRailItemStyle") };
        ToolTipService.SetToolTip(item, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, tooltip);
        item.Checked += (_, _) =>
        {
            if (_navigating is null)
            {
                Show(tag);
            }
        };
        _rail[tag] = item;
        return item;
    }

    private void Show(string tag)
    {
        var isBench = tag is "bench" or "benchresults";
        BenchTabs.Visibility = isBench ? Visibility.Visible : Visibility.Collapsed;
        if (isBench)
        {
            var tab = tag == "benchresults" ? BenchResultsTab : BenchRunTab;
            if (!ReferenceEquals(BenchTabs.SelectedItem, tab))
            {
                BenchTabs.SelectedItem = tab; // raises SelectionChanged, which navigates
                return;
            }
        }

        var page = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "inventory" => typeof(InventoryPage),
            "bench" => typeof(BenchmarkPage),
            "benchresults" => typeof(BenchmarkResultsPage),
            "network" => typeof(NetworkPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(SensorsPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
        }
    }

    /// <summary>Saves the window content as a PNG (the capture mode used by CI).</summary>
    public async Task SaveScreenshotAsync(string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            96,
            96,
            System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
        stream.Seek(0);
        await using var file = File.Create(path);
        await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }

    private void BenchTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            Show(tag);
        }
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
