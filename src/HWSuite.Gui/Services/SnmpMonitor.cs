using HWSuite.Core.Snmp;
using HWSuite.Providers.Windows;

namespace HWSuite.Gui.Services;

/// <summary>Live state of one watched device.</summary>
internal sealed class SnmpDeviceState
{
    public required SnmpDeviceConfig Config { get; set; }

    public SnmpPoll? Last { get; set; }

    public SnmpPoll? Previous { get; set; }

    public bool Online { get; set; }

    public string? Error { get; set; }

    public SnmpError? ErrorKind { get; set; }

    public int Failures { get; set; }

    public DateTimeOffset? LastOk { get; set; }

    /// <summary>Traffic history per port index: (in, out) Mbps, oldest first.</summary>
    public Dictionary<int, List<(double In, double Out)>> History { get; } = [];

    public IReadOnlyList<PortRate> Rates { get; set; } = [];
}

/// <summary>
/// Polls the SNMP devices from Settings in the background while HWSuite runs, keeps their traffic history and
/// raises events (device stopped answering, port down, speed dropped, errors), notified from the tray.
/// </summary>
internal sealed class SnmpMonitor : IDisposable
{
    public const int HistoryLength = 360;
    private const int DownAfterFailures = 3;

    private readonly Dictionary<string, (SnmpDeviceState State, CancellationTokenSource Stop)> _devices = [];
    private readonly List<SnmpEvent> _events = [];
    private readonly object _lock = new();

    /// <summary>Raised (on a background thread) after every reading of any device.</summary>
    public event EventHandler<string>? Updated;

    /// <summary>Raised for each new event.</summary>
    public event EventHandler<SnmpEvent>? EventRaised;

    public IReadOnlyList<SnmpDeviceState> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices.Values.Select(d => d.State)];
            }
        }
    }

    public IReadOnlyList<SnmpEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Starts, restarts or stops the loops so they match <paramref name="configs"/>.</summary>
    public void Apply(IReadOnlyList<SnmpDeviceConfig>? configs)
    {
        configs ??= [];
        lock (_lock)
        {
            foreach (var id in _devices.Keys.Where(id => configs.All(c => c.Id != id)).ToList())
            {
                _devices[id].Stop.Cancel();
                _devices.Remove(id);
            }

            foreach (var config in configs)
            {
                if (_devices.TryGetValue(config.Id, out var running))
                {
                    if (running.State.Config == config)
                    {
                        continue;
                    }

                    running.Stop.Cancel();
                    _devices.Remove(config.Id);
                }

                var state = new SnmpDeviceState { Config = config };
                var stop = new CancellationTokenSource();
                _devices[config.Id] = (state, stop);
                _ = Task.Run(() => LoopAsync(state, stop.Token));
            }
        }
    }

    /// <summary>One reading right away (the "Test" button in the device form).</summary>
    public static async Task<SnmpPoll> TestAsync(SnmpTarget target, CancellationToken cancel = default)
    {
        using var client = new SnmpClient(target with { TimeoutMs = 2500, Retries = 1 });
        return await SnmpReader.PollAsync(client, cancel).ConfigureAwait(false);
    }

    private async Task LoopAsync(SnmpDeviceState state, CancellationToken stop)
    {
        var target = state.Config.Target(Secrets.Unprotect);
        using var client = new SnmpClient(target);
        while (!stop.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                var poll = await SnmpReader.PollAsync(client, stop).ConfigureAwait(false);
                List<SnmpEvent> events;
                lock (_lock)
                {
                    events = [.. SnmpReader.Changes(state.Config.Id, state.Last, poll)];
                    if (!state.Online && state.Failures >= DownAfterFailures)
                    {
                        events.Insert(0, new SnmpEvent(poll.At, state.Config.Id, SnmpEventKind.DeviceUp, state.Config.Name, null, false));
                    }

                    state.Previous = state.Last;
                    state.Last = poll;
                    state.Online = true;
                    state.Error = null;
                    state.ErrorKind = null;
                    state.Failures = 0;
                    state.LastOk = poll.At;
                    if (state.Previous is { } previous)
                    {
                        state.Rates = SnmpReader.Rates(previous, poll);
                        foreach (var rate in state.Rates)
                        {
                            if (!state.History.TryGetValue(rate.Index, out var list))
                            {
                                state.History[rate.Index] = list = [];
                            }

                            list.Add((rate.InMbps, rate.OutMbps));
                            if (list.Count > HistoryLength)
                            {
                                list.RemoveAt(0);
                            }
                        }
                    }
                }

                Raise(events);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is SnmpException or System.Net.Sockets.SocketException or FormatException or ArgumentException or InvalidOperationException)
            {
                SnmpEvent? down = null;
                lock (_lock)
                {
                    state.Failures++;
                    state.Error = ex.Message;
                    state.ErrorKind = (ex as SnmpException)?.Error;
                    if (state.Failures == DownAfterFailures || (state.Failures >= DownAfterFailures && state.Online))
                    {
                        state.Online = false;
                        down = new SnmpEvent(DateTimeOffset.Now, state.Config.Id, SnmpEventKind.DeviceDown, state.Config.Name, ex.Message, true);
                    }
                }

                if (down is not null)
                {
                    Raise([down]);
                }
            }

            Updated?.Invoke(this, state.Config.Id);
            var wait = TimeSpan.FromSeconds(Math.Max(2, state.Config.PollSeconds)) - (DateTime.UtcNow - started);
            try
            {
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Raise(IReadOnlyList<SnmpEvent> events)
    {
        foreach (var e in events)
        {
            lock (_lock)
            {
                _events.Insert(0, e);
                if (_events.Count > 500)
                {
                    _events.RemoveAt(_events.Count - 1);
                }
            }

            EventRaised?.Invoke(this, e);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, stop) in _devices.Values)
            {
                stop.Cancel();
                stop.Dispose();
            }

            _devices.Clear();
        }
    }
}
