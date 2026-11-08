using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace HWSuite.Core.Network;

/// <summary>Rating of a connection for online games, from latency, jitter and loss.</summary>
public enum GameRating
{
    Unknown,
    Excellent,
    Good,
    Fair,
    Poor,
}

/// <summary>Latency statistics of the last pings to one host (null = no reply in the window).</summary>
public sealed record PingWindow(double? LastMs, double? AverageMs, double? MinMs, double? MaxMs, double? JitterMs, double LossPercent, int Sent, IReadOnlyList<double> History)
{
    public static PingWindow Empty { get; } = new(null, null, null, null, null, 0, 0, []);

    /// <summary>Statistics of a list of replies, oldest first; null = lost. History holds the replies (lost ones as 0 are left out).</summary>
    public static PingWindow From(IReadOnlyList<double?> replies)
    {
        ArgumentNullException.ThrowIfNull(replies);
        if (replies.Count == 0)
        {
            return Empty;
        }

        var ok = replies.Where(r => r is not null).Select(r => r!.Value).ToList();
        var loss = (replies.Count - ok.Count) * 100.0 / replies.Count;
        double? jitter = ok.Count >= 2 ? ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average() : null;
        return new PingWindow(
            replies[^1],
            ok.Count > 0 ? ok.Average() : null,
            ok.Count > 0 ? ok.Min() : null,
            ok.Count > 0 ? ok.Max() : null,
            jitter,
            loss,
            replies.Count,
            ok);
    }
}

/// <summary>One reading of the live network panel.</summary>
public sealed record LiveNetworkReading(
    NetConnection? Connection,
    string? Gateway,
    string Target,
    PingWindow Router,
    PingWindow Internet,
    double? DownMbps,
    double? UpMbps,
    IReadOnlyList<double> DownHistory,
    IReadOnlyList<double> UpHistory)
{
    /// <summary>
    /// Gaming rating of the Internet path: excellent below 30 ms / 5 ms jitter / no loss, poor from 100 ms,
    /// 30 ms jitter or 2 % loss.
    /// </summary>
    public GameRating Rating => Rate(Internet);

    public static GameRating Rate(PingWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.Sent < 3 || w.AverageMs is not { } avg)
        {
            return w.Sent >= 3 ? GameRating.Poor : GameRating.Unknown;
        }

        var jitter = w.JitterMs ?? 0;
        if (avg >= 100 || jitter >= 30 || w.LossPercent >= 2)
        {
            return GameRating.Poor;
        }

        if (avg >= 60 || jitter >= 15 || w.LossPercent > 0.5)
        {
            return GameRating.Fair;
        }

        return avg >= 30 || jitter >= 5 || w.LossPercent > 0 ? GameRating.Good : GameRating.Excellent;
    }
}

/// <summary>
/// Measures the connection continuously: one ping a second to the router and to an Internet host, adapter
/// throughput from its byte counters. Keeps the last minute.
/// </summary>
public sealed class LiveNetworkMonitor : IDisposable
{
    public const int Window = 60;

    private readonly Func<string, WifiLink?>? _wifi;
    private readonly Queue<double?> _router = new();
    private readonly Queue<double?> _internet = new();
    private readonly Queue<double> _down = new();
    private readonly Queue<double> _up = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private NetConnection? _connection;
    private string? _adapterId;
    private string? _gateway;
    private (long Rx, long Tx, long Ticks)? _bytes;
    private double? _downNow;
    private double? _upNow;
    private int _tick;

    public LiveNetworkMonitor(string target = "1.1.1.1", Func<string, WifiLink?>? wifi = null)
    {
        Target = target;
        _wifi = wifi;
    }

    public string Target { get; }

    public event EventHandler<LiveNetworkReading>? Updated;

    public bool Running => _run is not null;

    public void Start()
    {
        if (_run is not null)
        {
            return;
        }

        _run = new CancellationTokenSource();
        var token = _run.Token;
        _ = Task.Run(() => LoopAsync(token), token);
    }

    public void Stop()
    {
        _run?.Cancel();
        _run?.Dispose();
        _run = null;
    }

    public void Dispose() => Stop();

    public LiveNetworkReading Snapshot()
    {
        lock (_gate)
        {
            return new LiveNetworkReading(
                _connection,
                _gateway,
                Target,
                PingWindow.From([.. _router]),
                PingWindow.From([.. _internet]),
                _downNow,
                _upNow,
                [.. _down],
                [.. _up]);
        }
    }

    private async Task LoopAsync(CancellationToken cancel)
    {
        using var ping = new Ping();
        using var ping2 = new Ping();
        while (!cancel.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            if (_tick % 10 == 0)
            {
                RefreshAdapter();
            }

            var router = _gateway is { } gw ? PingOnce(ping, gw) : Task.FromResult<double?>(null);
            var internet = PingOnce(ping2, Target);
            await Task.WhenAll(router, internet).ConfigureAwait(false);
            lock (_gate)
            {
                if (_gateway is not null)
                {
                    Push(_router, router.Result);
                }

                Push(_internet, internet.Result);
                Throughput();
            }

            _tick++;
            Updated?.Invoke(this, Snapshot());
            var wait = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(started);
            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, cancel).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static void Push<T>(Queue<T> q, T v)
    {
        q.Enqueue(v);
        while (q.Count > Window)
        {
            q.Dequeue();
        }
    }

    private void RefreshAdapter()
    {
        var connection = ActiveConnection.Find(IpFamily.IPv4, _wifi);
        string? id = null;
        string? gateway = null;
        try
        {
            var adapter = connection is null ? null : NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == connection.Name);
            id = adapter?.Id;
            gateway = adapter?.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            // keep what we had
        }

        lock (_gate)
        {
            if (id != _adapterId)
            {
                _bytes = null;
                _router.Clear();
            }

            _connection = connection;
            _adapterId = id;
            _gateway = gateway;
        }
    }

    private void Throughput()
    {
        if (_adapterId is null)
        {
            return;
        }

        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == _adapterId);
            if (adapter is null)
            {
                return;
            }

            var stats = adapter.GetIPStatistics();
            var now = (stats.BytesReceived, stats.BytesSent, Stopwatch.GetTimestamp());
            if (_bytes is { } last)
            {
                var seconds = Stopwatch.GetElapsedTime(last.Ticks, now.Item3).TotalSeconds;
                if (seconds > 0.2)
                {
                    _downNow = Math.Max(0, (now.BytesReceived - last.Rx) * 8 / seconds / 1e6);
                    _upNow = Math.Max(0, (now.BytesSent - last.Tx) * 8 / seconds / 1e6);
                    Push(_down, _downNow.Value);
                    Push(_up, _upNow.Value);
                }
            }

            _bytes = now;
        }
        catch (NetworkInformationException)
        {
            // counters not available
        }
    }

    private static async Task<double?> PingOnce(Ping ping, string host)
    {
        try
        {
            var reply = await ping.SendPingAsync(host, 900).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? Math.Max(reply.RoundtripTime, 0.5) : null;
        }
        catch (PingException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
