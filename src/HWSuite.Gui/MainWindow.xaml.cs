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
        var accent = Hud.HudAccents.Of(App.Settings.Current.Accent);
        var text = Hud.HudSkins.Parse(App.Skin.TitleButtons);
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = Hud.HudSkins.Parse(App.Skin.TitleButtonsInactive);
        bar.ButtonHoverBackgroundColor = Hud.HudAccents.WithAlpha(accent, 0x30);
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedBackgroundColor = Hud.HudAccents.WithAlpha(accent, 0x50);
        TitleVersion.Text = $"// {BuildInfo.Current.Display}".ToUpper(s.Culture);
        TitleMode.Text = App.IsDemo ? "// DEMO" : Core.Settings.DataLocation.IsPortable ? "// " + s["kit.portableTag"].ToUpper(s.Culture) : "";

        AppWindow.Closing += AppWindow_Closing;
        Activated += (_, _) => _isShown = AppWindow.IsVisible;

        // The theme decides where the pages are listed: narrow rail, wide side menu or tabs on top.
        var nav = Hud.HudThemes.Current.Nav;
        if (nav == Hud.NavMode.Wide)
        {
            RailColumn.Width = new GridLength(232);
        }
        else if (nav == Hud.NavMode.Top)
        {
            RailColumn.Width = new GridLength(0);
            RailBorder.Visibility = Visibility.Collapsed;
            TopNavBar.Visibility = Visibility.Visible;
        }

        void Top(RadioButton item) => (nav == Hud.NavMode.Top ? TopNav : RailTop).Children.Add(item);
        void Bottom(RadioButton item) => (nav == Hud.NavMode.Top ? TopNav : RailBottom).Children.Add(item);
        Top(RailItem("dashboard", "\uF246", s["rail.dashboard"], s["rail.dashboard.tip"]));
        Top(RailItem("inventory", "\uE950", s["rail.hardware"], s["app.nav.inventory"]));
        Top(RailItem("sensors", "\uE9D9", s["rail.sensors"], s["app.nav.sensors"]));
        Top(RailItem("charts", "\uE9D2", s["rail.charts"], s["charts.title"]));
        Top(RailItem("bench", "\uEC4A", s["rail.bench"], s["app.nav.bench"]));
        Top(RailItem("network", "\uE701", s["rail.network"], s["net.title"]));
        Top(RailItem("devices", "\uE968", s["rail.devices"], s["snmp.title"]));
        Top(RailItem("tuneup", "\uE90F", s["rail.maintenance"], s["tune.title"]));
        Top(RailItem("startup", "\uE7E8", s["rail.startup"], s["startup.title"]));
        Top(RailItem("report", "\uE9F9", s["rail.report"], s["mnt.report.title"]));
        Bottom(RailItem("settings", "\uE713", s["rail.settings"], s["app.nav.settings"]));
        Bottom(RailItem("about", "\uE946", s["rail.about"], s["app.nav.about"]));
        BenchRunTab.Text = s["app.nav.benchRun"];
        StabilityTab.Text = s["stab.tab"];
        ReportRunTab.Text = s["hist.tab.report"];
        HistoryTab.Text = s["hist.tab.history"];
        NetInternetTab.Text = s["net.tab.internet"];
        WifiTab.Text = s["wifi.tab"];
        BenchResultsTab.Text = s["app.nav.benchResults"];
        Navigate("dashboard");

        UpdateAccessBar(App.Monitor.Access);
    }

    /// <summary>Opens a page by its tag ("dashboard", "sensors", "inventory", "bench", "benchresults", "network", "report", "settings", "about").</summary>
    public void Navigate(string tag)
    {
        var railTag = tag switch
        {
            "benchresults" or "stability" => "bench",
            "history" => "report",
            "wifi" => "network",
            _ => tag,
        };
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
        var nav = Hud.HudThemes.Current.Nav;
        if (nav != Hud.NavMode.Rail)
        {
            // Wide menu and top tabs: icon and the name in a row.
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = nav == Hud.NavMode.Wide ? 14 : 8 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = nav == Hud.NavMode.Wide ? 16 : 14, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock
            {
                Text = nav == Hud.NavMode.Wide ? label : label.ToUpper(App.Text.Culture),
                FontSize = nav == Hud.NavMode.Wide ? 14 : 12.5,
                CharacterSpacing = nav == Hud.NavMode.Wide ? 0 : 40,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var wide = new RadioButton { Content = row, Tag = tag, Style = Hud.HudUi.Style(nav == Hud.NavMode.Wide ? "HudWideItemStyle" : "HudTopItemStyle") };
            ToolTipService.SetToolTip(wide, tooltip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(wide, tooltip);
            wide.Checked += (_, _) =>
            {
                if (_navigating is null)
                {
                    Show(tag);
                }
            };
            _rail[tag] = wide;
            return wide;
        }

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = label.ToUpper(App.Text.Culture),
            FontFamily = Hud.HudUi.Font("HudMonoFont"),
            // Long names ("DISPOSITIVOS", "MANUTENÇÃO") a bit smaller so every label fits with room on both sides.
            FontSize = label.Length > 10 ? 9.5 : label.Length > 8 ? 10 : 10.5,
            CharacterSpacing = label.Length > 8 ? 10 : 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 94,
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
        // Pages grouped under one rail item get tabs: Bench (tests | stability | results), Report (new | history).
        var isBench = tag is "bench" or "stability" or "benchresults";
        var isReport = tag is "report" or "history";
        var isNet = tag is "network" or "wifi";
        BenchTabs.Visibility = isBench ? Visibility.Visible : Visibility.Collapsed;
        ReportTabs.Visibility = isReport ? Visibility.Visible : Visibility.Collapsed;
        NetTabs.Visibility = isNet ? Visibility.Visible : Visibility.Collapsed;
        if (isBench || isReport || isNet)
        {
            var bar = isBench ? BenchTabs : isReport ? ReportTabs : NetTabs;
            var tab = tag switch
            {
                "benchresults" => BenchResultsTab,
                "stability" => StabilityTab,
                "history" => HistoryTab,
                "report" => ReportRunTab,
                "network" => NetInternetTab,
                "wifi" => WifiTab,
                _ => BenchRunTab,
            };
            if (!ReferenceEquals(bar.SelectedItem, tab))
            {
                bar.SelectedItem = tab; // raises SelectionChanged, which navigates
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
            "wifi" => typeof(WifiPage),
            "devices" => typeof(DevicesPage),
            "report" => typeof(ReportPage),
            "history" => typeof(HistoryPage),
            "tuneup" => typeof(TuneupPage),
            "startup" => typeof(StartupPage),
            "stability" => typeof(StabilityPage),
            "charts" => typeof(ChartsPage),
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

    /// <summary>Installed by the setup (or winget, which runs the same setup): its uninstaller sits next to the app.</summary>
    private static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>
    /// "HWSuite 0.20.0 is available" with "Update now". The update is always done by the app itself: an installed
    /// copy downloads the setup (checked against GitHub's SHA-256), installs it silently and restarts; a portable copy
    /// downloads the desktop app zip (checked the same way) and replaces itself in its own folder, then restarts.
    /// </summary>
    public void ShowUpdate(Core.Updates.ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var s = App.Text;
        _update = release;
        UpdateBar.Title = s.Format("app.update.title", release.Version);
        UpdateBar.Message = s.Format("app.update.message", Core.BuildInfo.Current.Version);
        UpdateButton.Content = s[CanUpdate(release) ? "app.update.now" : "app.update.page"];
        UpdateButton.IsEnabled = true;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.IsOpen = true;
    }

    private static bool CanUpdate(Core.Updates.ReleaseInfo release) => IsInstalled
        ? release.Installer is not null && release.InstallerSha256 is not null
        : release.Portable is not null && release.PortableSha256 is not null;

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } release || _updating)
        {
            return;
        }

        if (!CanUpdate(release))
        {
            // Only a release published without its files (or their checksums) ends here.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return;
        }

        var s = App.Text;
        _updating = true;
        UpdateButton.IsEnabled = false;
        var progress = new Progress<double>(p => UpdateBar.Message = s.Format("app.update.downloading", (p * 100).ToString("0", s.Culture)));
        var folder = Path.Combine(Path.GetTempPath(), "HWSuite-update");
        try
        {
            if (!IsInstalled && !Services.PortableUpdater.CanWrite(AppContext.BaseDirectory))
            {
                throw new UnauthorizedAccessException(s["app.update.readOnly"]);
            }

            UpdateBar.Message = s.Format("app.update.downloading", "0");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var file = IsInstalled
                ? await Core.Updates.UpdateChecker.DownloadInstallerAsync(http, release, folder, progress)
                : await Core.Updates.UpdateChecker.DownloadPortableAsync(http, release, folder, progress);
            if (file is null)
            {
                throw new IOException(s["app.update.downloadFailed"]);
            }

            UpdateBar.Message = s["app.update.installing"];
            if (IsInstalled)
            {
                // Silent install over this copy; the setup starts the new version when it finishes (/RELAUNCH=1).
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1") { UseShellExecute = true })?.Dispose();
            }
            else
            {
                await Task.Run(() => Services.PortableUpdater.Apply(file, AppContext.BaseDirectory, "HWSuite.Gui.exe"));
            }

            App.Quit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception or HttpRequestException)
        {
            // Stays inside the app: the button tries again, nothing is handed to the browser.
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Message = s.Format("app.update.failedRetry", ex.Message);
            UpdateButton.Content = s["app.update.retry"];
            UpdateButton.IsEnabled = true;
            _updating = false;
        }
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
            App.DriverInstalledThisSession = true;
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
