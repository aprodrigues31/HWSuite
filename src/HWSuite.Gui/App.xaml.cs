using HWSuite.Core.Alerts;
using HWSuite.Core.Localization;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace HWSuite.Gui;

public partial class App : Application
{
    /// <summary>Passed by the elevation relaunch: the old (non-elevated) instance is still closing.</summary>
    public const string RelaunchArgument = "--relaunched";

    private const string InstanceMutexName = @"Local\HWSuite.Gui.Instance";

    private Mutex? _instance;
    private DispatcherQueue? _ui;
    private DateTime _lastTooltip;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>UI text in the system language (Portuguese or English).</summary>
    public static Strings Text { get; } = Strings.For((string?)null);

    public static SettingsService Settings { get; } = new();

    /// <summary>Shared sensor polling, used by every page that shows live values.</summary>
    public static SensorMonitor Monitor { get; } = new();

    public static MainWindow? MainWindow { get; private set; }

    public static TrayIcon? Tray { get; private set; }

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "HWSuite.ico");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        if (!AcquireSingleInstance(commandLine.Contains(RelaunchArgument, StringComparer.OrdinalIgnoreCase)))
        {
            TrayIcon.SignalRunningInstance(); // bring the running copy to the front instead
            Exit();
            return;
        }

        _ui = DispatcherQueue.GetForCurrentThread();
        Monitor.Configure(Settings.Current, Settings.Rules, Settings.LogFolder);
        Settings.Changed += (_, s) => Monitor.Configure(s, Settings.Rules, Settings.LogFolder);
        Monitor.Alerted += OnAlerted;
        Monitor.Updated += OnUpdated;

        Tray = new TrayIcon(IconPath, Text["app.tray.open"], Text["app.tray.exit"]);
        Tray.OpenRequested += (_, _) => MainWindow?.ShowFromTray();
        Tray.ExitRequested += (_, _) => Quit();

        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => Shutdown();

        var minimized = commandLine.Contains(StartupTask.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        if (!minimized || !Tray.IsAdded)
        {
            MainWindow.Activate();
        }

        Monitor.Start();
    }

    /// <summary>Exits for real (tray menu "Exit"), bypassing close-to-tray.</summary>
    public static void Quit()
    {
        if (MainWindow is { } window)
        {
            window.AllowClose();
            window.Close();
        }

        Current.Exit();
    }

    private static void Shutdown()
    {
        Monitor.Dispose();
        Tray?.Dispose();
        Tray = null;
    }

    private bool AcquireSingleInstance(bool relaunched)
    {
        _instance = new Mutex(initiallyOwned: false, InstanceMutexName);
        try
        {
            // After "reopen as administrator" the old copy needs a moment to exit and release it.
            return _instance.WaitOne(relaunched ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true; // previous instance crashed; the mutex is ours now
        }
    }

    private void OnUpdated(object? sender, SensorSnapshot snapshot)
    {
        if (DateTime.UtcNow - _lastTooltip < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastTooltip = DateTime.UtcNow;
        var tip = SensorSummary.Tooltip(snapshot, Text);
        _ui?.TryEnqueue(() => Tray?.SetTooltip(tip));
    }

    private void OnAlerted(object? sender, IReadOnlyList<AlertEvent> events)
    {
        var c = Text.Culture;
        foreach (var e in events.Where(e => e.Change == AlertChange.Raised))
        {
            var sensor = $"{e.Reading.Name} ({e.Reading.Hardware})";
            var message = Text.Format("app.alert.raised", sensor,
                SensorFormat.Format(e.Value, e.Reading.Kind, c),
                SensorFormat.Format(e.Rule.Threshold, e.Reading.Kind, c));
            var title = e.Rule.Label.Length > 0 ? e.Rule.Label : sensor;
            _ui?.TryEnqueue(() => Tray?.Notify(title, message, warning: true));
        }
    }
}
