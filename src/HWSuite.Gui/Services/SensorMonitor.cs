using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;

namespace HWSuite.Gui.Services;

/// <summary>
/// Polls the sensor source on a background thread at the configured interval, keeps a history for
/// charts, evaluates alert rules and optionally writes a CSV log. It keeps running while the window
/// is hidden in the notification area. Events are raised on the polling thread.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly AlertEvaluator _alerts = new();
    private ISensorSource? _source;
    private Timer? _timer;
    private int _busy;
    private StreamWriter? _logWriter;
    private SensorCsvLogger? _logger;
    private AppSettings _settings = new();
    private IReadOnlyList<AlertRule> _rules = [];

    public SensorHistory History { get; } = new(capacity: 300);

    /// <summary>The last hour of readings with their time, for the Charts page.</summary>
    public SensorTimeline Timeline { get; } = new(capacity: 3600);

    /// <summary>Creates the sensor source; replaced by the demo mode before monitoring starts.</summary>
    public static Func<AccessStatus, ISensorSource> SourceFactory { get; set; } = access => ProviderCatalog.CreateSensorSource(access);

    public AccessStatus Access { get; private set; } = AccessStatus.Detect();

    public SensorSnapshot? Latest { get; private set; }

    /// <summary>Current CSV log file, or null when logging is off.</summary>
    public string? LogPath { get; private set; }

    public TimeSpan Interval => TimeSpan.FromSeconds(_settings.IntervalSeconds);

    public event EventHandler<SensorSnapshot>? Updated;

    public event EventHandler<Exception>? Failed;

    public event EventHandler<IReadOnlyList<AlertEvent>>? Alerted;

    /// <summary>A subscriber (page, tray) threw while handling an update; reading itself worked.</summary>
    public event EventHandler<Exception>? SubscriberFailed;

    /// <summary>Applies interval, alert and log settings without reopening the hardware.</summary>
    public void Configure(AppSettings settings, IReadOnlyList<AlertRule> rules, string logFolder)
    {
        lock (_gate)
        {
            var intervalChanged = settings.IntervalSeconds != _settings.IntervalSeconds;
            _settings = settings;
            _rules = settings.AlertsEnabled ? rules : [];
            if (intervalChanged)
            {
                _timer?.Change(Interval, Interval);
            }

            if (settings.LogEnabled && _logWriter is null)
            {
                OpenLog(logFolder);
            }
            else if (!settings.LogEnabled)
            {
                CloseLog();
            }
        }
    }

    /// <summary>(Re)opens the hardware with the current access level and starts polling.</summary>
    public void Start()
    {
        Stop();
        foreach (var snapshot in _seed)
        {
            History.Add(snapshot);
            Timeline.Add(snapshot);
        }

        Access = AccessStatus.Detect();
        var access = Access;
        _ = Task.Run(() =>
        {
            try
            {
                var source = SourceFactory(access);
                lock (_gate)
                {
                    _source = source;
                    _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
                }
            }
            catch (Exception ex)
            {
                Failed?.Invoke(this, ex);
            }
        });
    }

    private IReadOnlyList<SensorSnapshot> _seed = [];

    /// <summary>
    /// Pre-fills the history (demo mode) so charts start full. Call before <see cref="Start"/>: the samples go in
    /// after the history is reset and before the first live read, so the timeline stays in time order.
    /// </summary>
    public void Seed(IEnumerable<SensorSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _seed = [.. snapshots];
    }

    public void Stop()
    {
        Timer? timer;
        ISensorSource? source;
        lock (_gate)
        {
            timer = _timer;
            source = _source;
            _timer = null;
            _source = null;
        }

        if (timer is not null)
        {
            using var done = new ManualResetEvent(false);
            if (timer.Dispose(done))
            {
                done.WaitOne(TimeSpan.FromSeconds(3)); // let an in-flight read finish before closing handles
            }
        }

        source?.Dispose();
        History.Clear();
        Timeline.Clear();
        Latest = null;
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            CloseLog();
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return; // a slow read is still running; skip this tick
        }

        try
        {
            ISensorSource? source;
            lock (_gate)
            {
                source = _source;
            }

            if (source is null)
            {
                return;
            }

            // The pump's fan header gets its name before anyone (history, alerts, log, pages) sees the reading.
            var snapshot = PowerRails.NamePump(source.Read(), _settings.PumpSensorId, App.Text);
            History.Add(snapshot);
            Timeline.Add(snapshot);
            Latest = snapshot;

            IReadOnlyList<AlertEvent> events;
            lock (_gate)
            {
                events = _alerts.Evaluate(snapshot, _rules);
                WriteLog(snapshot);
            }

            Notify(Updated, snapshot);
            if (events.Count > 0)
            {
                Notify(Alerted, events);
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>
    /// Calls each subscriber separately: a bug in one page must neither stop the others nor be
    /// reported as "sensors could not start".
    /// </summary>
    private void Notify<T>(EventHandler<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(this, value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HWSuite: sensor subscriber failed: {ex}");
                SubscriberFailed?.Invoke(this, ex);
            }
        }
    }

    private void OpenLog(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"sensors-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _logWriter = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _logger = new SensorCsvLogger(_logWriter);
            LogPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CloseLog();
            Failed?.Invoke(this, ex);
        }
    }

    private void WriteLog(SensorSnapshot snapshot)
    {
        if (_logger is null)
        {
            return;
        }

        try
        {
            _logger.Append(snapshot);
        }
        catch (IOException ex)
        {
            CloseLog(); // disk full or removed: stop logging instead of failing every tick
            Failed?.Invoke(this, ex);
        }
    }

    private void CloseLog()
    {
        _logWriter?.Dispose();
        _logWriter = null;
        _logger = null;
        LogPath = null;
    }
}
