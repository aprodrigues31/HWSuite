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

    /// <summary>CI check: run normally for a few seconds, then write what the sensors did to a file and exit.</summary>
    public const string SelfTestArgument = "--self-test";

    /// <summary>Shows plausible made-up readings instead of the real sensors (screenshots, machines without access).</summary>
    public const string DemoArgument = "--demo";

    /// <summary>CI: opens every page in turn, saves each as a PNG into the given folder, then exits.</summary>
    public const string CaptureArgument = "--capture";

    private const string InstanceMutexName = @"Local\HWSuite.Gui.Instance";

    private Mutex? _instance;
    private DispatcherQueue? _ui;
    private DateTime _lastTooltip;
    private int _updates;

    // DispatcherQueueTimers must stay referenced: a timer held only by a local variable can be
    // garbage collected and then never fires (this made the CI self-test "hang" intermittently).
    private readonly List<DispatcherQueueTimer> _timers = [];
    private string? _lastError;

    public App()
    {
        InitializeComponent();
    }

    // Settings first: the UI language is one of them.
    public static SettingsService Settings { get; } = new();

    /// <summary>UI text in the chosen language, or the system language (Portuguese or English); applied at start.</summary>
    public static Strings Text { get; } = Strings.For(Settings.Current.Language);

    /// <summary>Shared sensor polling, used by every page that shows live values.</summary>
    public static SensorMonitor Monitor { get; } = new();

    public static MainWindow? MainWindow { get; private set; }

    public static TrayIcon? Tray { get; private set; }

    /// <summary>Running on <see cref="DemoSensorSource"/> data (title bar says DEMO; nothing is saved as a real reading).</summary>
    public static bool IsDemo { get; private set; }

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
        IsDemo = commandLine.Contains(DemoArgument, StringComparer.OrdinalIgnoreCase);
        if (IsDemo)
        {
            // Five minutes of history from the start, so charts are full; the live source continues from there.
            SensorMonitor.SourceFactory = _ => new MinMaxTrackingSource(new DemoSensorSource(elapsedAtStart: TimeSpan.FromMinutes(5)));
        }

        Hud.HudAccents.Apply(Settings.Current.Accent);
        Monitor.Configure(Settings.Current, Settings.EffectiveRules, Settings.LogFolder);
        Settings.Changed += (_, s) => Monitor.Configure(s, Settings.EffectiveRules, Settings.LogFolder);
        Monitor.Alerted += OnAlerted;
        Monitor.Updated += OnUpdated;
        Monitor.Failed += (_, ex) => _lastError ??= ex.ToString();
        Monitor.SubscriberFailed += (_, ex) => _lastError ??= ex.ToString();

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

        if (IsDemo)
        {
            Monitor.Seed(DemoSensorSource.History(TimeSpan.FromMinutes(15), Monitor.Interval)); // Charts page shows 15 min
        }

        Monitor.Start();

        var capture = Array.FindIndex(commandLine, a => a.Equals(CaptureArgument, StringComparison.OrdinalIgnoreCase));
        if (capture >= 0 && capture + 1 < commandLine.Length)
        {
            _ = CaptureAsync(commandLine[capture + 1]);
            return;
        }

        var selfTest = Array.FindIndex(commandLine, a => a.Equals(SelfTestArgument, StringComparison.OrdinalIgnoreCase));
        if (selfTest >= 0 && selfTest + 1 < commandLine.Length)
        {
            RunSelfTest(commandLine[selfTest + 1]);
        }
        else if (Settings.Current.CheckForUpdates)
        {
            _ = CheckForUpdatesAsync();
        }
    }

    /// <summary>One request to GitHub a few seconds after start; a newer release shows a bar in the window.</summary>
    private async Task CheckForUpdatesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await FindUpdateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Asks GitHub for the latest release. Returns it with whether it is newer (the window then shows the
    /// update bar), or null when GitHub could not be reached. Used at start and by the "check now" buttons.
    /// </summary>
    public static async Task<(Core.Updates.ReleaseInfo Release, bool Newer)?> FindUpdateAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var latest = await Core.Updates.UpdateChecker.LatestAsync(http).ConfigureAwait(true);
        if (latest is null)
        {
            return null;
        }

        var newer = Core.Updates.UpdateChecker.IsNewer(latest.Version, Core.BuildInfo.Current.Version);
        if (newer)
        {
            MainWindow?.ShowUpdate(latest);
        }

        return (latest, newer);
    }

    /// <summary>
    /// Passes when the sensors delivered several updates to a visible window with no error. The
    /// report lists the first error, so CI shows why the app would say "sensors could not start".
    /// </summary>
    private void RunSelfTest(string reportPath)
    {
        File.WriteAllText(reportPath, "result: started\n"); // proves the app reached this point if it never finishes

        // UI heartbeat: if the UI thread stops running queued work, the watchdog below reports it.
        var lastBeat = DateTime.UtcNow.Ticks;
        var heartbeat = _ui!.CreateTimer();
        heartbeat.Interval = TimeSpan.FromSeconds(1);
        heartbeat.Tick += (_, _) => Interlocked.Exchange(ref lastBeat, DateTime.UtcNow.Ticks);
        heartbeat.Start();
        _timers.Add(heartbeat);

        string Report(string result) => $"""
            result: {result}
            updates: {_updates}
            sensors: {Monitor.Latest?.Readings.Count ?? 0}
            tray icon: {Tray?.IsAdded}
            ui thread idle for: {(DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastBeat), DateTimeKind.Utc)).TotalSeconds:0.0} s
            error: {_lastError ?? "none"}
            """;

        var done = 0;
        var timer = _ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(12);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            Interlocked.Exchange(ref done, 1);
            var latest = Monitor.Latest;
            var ok = _updates >= 3 && _lastError is null && latest is { Readings.Count: > 0 };
            File.WriteAllText(reportPath, Report(ok ? "pass" : "fail") + $"\nwindow shown: {MainWindow?.IsShown}\ntooltip: {SensorSummary.Tooltip(latest, Text)}\n");
            Environment.Exit(ok ? 0 : 3);
        };
        timer.Start();
        _timers.Add(timer);

        // Watchdog on its own thread: still reports when the UI thread is stuck (the timer above then never fires).
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(25));
            if (Volatile.Read(ref done) == 0)
            {
                File.WriteAllText(reportPath, Report("ui-thread-blocked"));
            }
        })
        { IsBackground = true, Name = "HWSuite self-test watchdog" }.Start();
    }

    /// <summary>
    /// Saves a PNG of every page (window content, 1440 × 900) into <paramref name="folder"/> and exits; CI publishes
    /// them so the interface can be reviewed without a Windows machine. Exit code 0 when every page was saved.
    /// </summary>
    private async Task CaptureAsync(string folder)
    {
        string[] pages = ["dashboard", "sensors", "charts", "inventory", "bench", "benchresults", "network", "report", "settings", "about"];
        var saved = 0;
        try
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true); // window shown, first sensor reads in
            for (var i = 0; i < pages.Length; i++)
            {
                MainWindow!.Navigate(pages[i]);
                await Task.Delay(TimeSpan.FromSeconds(pages[i] == "report" ? 20 : 4)).ConfigureAwait(true); // the report reads the hardware first
                await MainWindow.SaveScreenshotAsync(Path.Combine(folder, $"{i + 1:00}-{pages[i]}.png")).ConfigureAwait(true);
                saved++;
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "capture-error.txt"), ex.ToString());
        }

        Environment.Exit(saved == pages.Length ? 0 : 4);
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
        Interlocked.Increment(ref _updates);
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
