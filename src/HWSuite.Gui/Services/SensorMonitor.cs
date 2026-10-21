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

    public AccessStatus Access { get; private set; } = AccessStatus.Detect();

    public SensorSnapshot? Latest { get; private set; }

    /// <summary>Current CSV log file, or null when logging is off.</summary>
    public string? LogPath { get; private set; }

    public TimeSpan Interval => TimeSpan.FromSeconds(_settings.IntervalSeconds);

    public event EventHandler<SensorSnapshot>? Updated;

    public event EventHandler<Exception>? Failed;

    public event EventHandler<IReadOnlyList<AlertEvent>>? Alerted;

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
        Access = AccessStatus.Detect();
        var access = Access;
        _ = Task.Run(() =>
        {
            try
            {
                var source = ProviderCatalog.CreateSensorSource(access);
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

            var snapshot = source.Read();
            History.Add(snapshot);
            Latest = snapshot;

            IReadOnlyList<AlertEvent> events;
            lock (_gate)
            {
                events = _alerts.Evaluate(snapshot, _rules);
                WriteLog(snapshot);
            }

            Updated?.Invoke(this, snapshot);
            if (events.Count > 0)
            {
                Alerted?.Invoke(this, events);
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
