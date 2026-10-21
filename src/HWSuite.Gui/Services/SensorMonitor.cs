using HWSuite.Core.Sensors;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;

namespace HWSuite.Gui.Services;

/// <summary>
/// Polls the sensor source once per second on a background thread and keeps a 5-minute history.
/// Events are raised on that background thread; pages marshal to the UI thread themselves.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly object _gate = new();
    private ISensorSource? _source;
    private Timer? _timer;
    private int _busy;

    public SensorHistory History { get; } = new(capacity: 300);

    public AccessStatus Access { get; private set; } = AccessStatus.Detect();

    public SensorSnapshot? Latest { get; private set; }

    public event EventHandler<SensorSnapshot>? Updated;

    public event EventHandler<Exception>? Failed;

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
                    _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
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

    public void Dispose() => Stop();

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
            Updated?.Invoke(this, snapshot);
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
}
