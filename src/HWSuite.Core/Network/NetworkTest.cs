using System.Diagnostics;

namespace HWSuite.Core.Network;

/// <summary>
/// The connection to one test server. Every connection a probe opens goes to the address it resolved for the
/// chosen IP version, so a test is purely IPv4 or purely IPv6.
/// </summary>
public interface INetworkProbe : IDisposable
{
    /// <summary>Server, provider and public address known so far (the M-Lab address arrives during the download).</summary>
    NetEndpoint Endpoint { get; }

    /// <summary>Finds the server and checks it answers over the chosen IP version; throws <see cref="NetworkTestException"/>.</summary>
    Task PrepareAsync(CancellationToken cancel);

    /// <summary>One round trip in ms (a TCP handshake to the server), or null when no answer came in time (a lost packet).</summary>
    Task<double?> RttAsync(CancellationToken cancel);

    /// <summary>Receives as fast as possible until cancelled (or the server ends), reporting bytes as they arrive.</summary>
    Task DownloadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel);

    /// <summary>Sends as fast as possible until cancelled (or the server ends), reporting bytes as they leave.</summary>
    Task UploadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel);
}

/// <summary>Progress of a test: the phase ("prepare", "latency", "download", "upload", "done"), 0..1 and the current value.</summary>
public readonly record struct NetProgress(string Phase, double Fraction, double? Value);

/// <summary>Timing of a test; the defaults are what the app and the CLI use, tests shorten them.</summary>
public sealed record NetTestOptions
{
    /// <summary>Probes without load: the idle latency and jitter, and the packet loss (100 probes: 1% resolution).</summary>
    public int IdleSamples { get; init; } = 100;

    public TimeSpan IdleInterval { get; init; } = TimeSpan.FromMilliseconds(25);

    public TimeSpan PhaseDuration { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan LoadedRttInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Internet speed test in the style of the common speed test sites: latency without load, then download and
/// upload for a fixed time while latency keeps being measured (the latency "under load", which shows
/// bufferbloat). Throughput is sampled every 100 ms; the headline speed is the median sample.
/// Packet loss is measured without load, like SIMET and Speedtest do: the share of the idle probes (TCP
/// handshakes, which servers answer reliably, unlike ping) that got no answer. Under load a saturated link
/// drops packets on purpose (that is how TCP finds the available speed), so that would not be "loss".
/// </summary>
public static class NetworkTest
{
    public const string Version = "Net v2";

    public static async Task<NetworkTestResult> RunAsync(
        INetworkProbe probe,
        NetServer server,
        IpFamily family,
        NetConnection? connection,
        NetTestOptions? options = null,
        IProgress<NetProgress>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        options ??= new NetTestOptions();

        progress?.Report(new("prepare", 0, null));
        await probe.PrepareAsync(cancel).ConfigureAwait(false);

        var (idle, sent) = await IdleAsync(probe, options, progress, cancel).ConfigureAwait(false);
        if (idle.Count == 0)
        {
            throw new NetworkTestException("net.error.unreachable");
        }

        var (down, downRtt) = await LoadAsync(probe, probe.DownloadAsync, "download", 0.15, options, progress, cancel).ConfigureAwait(false);
        var (up, upRtt) = await LoadAsync(probe, probe.UploadAsync, "upload", 0.575, options, progress, cancel).ConfigureAwait(false);
        progress?.Report(new("done", 1, null));
        return new NetworkTestResult(
            Version,
            server,
            family,
            probe.Endpoint,
            down,
            up,
            new LatencyPhase(idle),
            new LatencyPhase(downRtt),
            new LatencyPhase(upRtt),
            NetStats.Loss(sent, idle.Count),
            connection,
            DateTimeOffset.Now);
    }

    private static async Task<(List<NetSample> Samples, int Sent)> IdleAsync(INetworkProbe probe, NetTestOptions options, IProgress<NetProgress>? progress, CancellationToken cancel)
    {
        var samples = new List<NetSample>();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < options.IdleSamples; i++)
        {
            if (await probe.RttAsync(cancel).ConfigureAwait(false) is { } rtt)
            {
                samples.Add(new NetSample(clock.Elapsed.TotalSeconds, rtt));
                progress?.Report(new("latency", 0.05 + (0.1 * (i + 1) / options.IdleSamples), rtt));
            }

            await Task.Delay(options.IdleInterval, cancel).ConfigureAwait(false);
        }

        return (samples, options.IdleSamples);
    }

    /// <summary>Runs one transfer direction for the phase duration, sampling throughput and loaded latency.</summary>
    private static async Task<(List<NetSample> Throughput, List<NetSample> Rtt)> LoadAsync(
        INetworkProbe probe,
        Func<Action<long>, TimeSpan, CancellationToken, Task> transfer,
        string phase,
        double startFraction,
        NetTestOptions options,
        IProgress<NetProgress>? progress,
        CancellationToken cancel)
    {
        long total = 0;
        var throughput = new List<NetSample>();
        var rtts = new List<NetSample>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var clock = Stopwatch.StartNew();
        var duration = options.PhaseDuration;
        var running = transfer(n => Interlocked.Add(ref total, n), duration, stop.Token);
        var rttLoop = LoadedRttAsync(probe, rtts, clock, options.LoadedRttInterval, stop.Token);

        long lastBytes = 0;
        var lastTime = 0.0;
        while (clock.Elapsed < duration && !running.IsCompleted)
        {
            await Task.WhenAny(running, Task.Delay(options.SampleInterval, cancel)).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            var now = clock.Elapsed.TotalSeconds;
            var bytes = Interlocked.Read(ref total);
            if (now - lastTime >= options.SampleInterval.TotalSeconds / 2)
            {
                var mbps = (bytes - lastBytes) * 8 / 1e6 / (now - lastTime);
                throughput.Add(new NetSample(now, mbps));
                progress?.Report(new(phase, startFraction + (0.425 * Math.Min(1, now / duration.TotalSeconds)), mbps));
                lastBytes = bytes;
                lastTime = now;
            }
        }

        await stop.CancelAsync().ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            await running.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
        }

        try
        {
            await rttLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
        }

        cancel.ThrowIfCancellationRequested();
        if (Interlocked.Read(ref total) == 0)
        {
            throw failure as NetworkTestException ?? new NetworkTestException("net.error.transfer", failure ?? new IOException("No data"));
        }

        lock (rtts)
        {
            return (throughput, [.. rtts]);
        }
    }

    private static async Task LoadedRttAsync(INetworkProbe probe, List<NetSample> rtts, Stopwatch clock, TimeSpan interval, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            if (await probe.RttAsync(cancel).ConfigureAwait(false) is { } rtt)
            {
                lock (rtts)
                {
                    rtts.Add(new NetSample(clock.Elapsed.TotalSeconds, rtt));
                }
            }

            await Task.Delay(interval, cancel).ConfigureAwait(false);
        }
    }
}
