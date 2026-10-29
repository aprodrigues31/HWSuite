namespace HWSuite.Core.Network;

/// <summary>Which public test server measures the connection.</summary>
public enum NetServer
{
    /// <summary>Cloudflare's speed test endpoints (speed.cloudflare.com), nearest data center by anycast.</summary>
    Cloudflare,

    /// <summary>Measurement Lab's ndt7 servers; every test is published in M-Lab's open data set.</summary>
    MLab,
}

/// <summary>IP version used for every connection of a test.</summary>
public enum IpFamily
{
    IPv4,
    IPv6,
}

/// <summary>One sample of a time series: seconds since the phase started and the value (Mbps or ms).</summary>
public readonly record struct NetSample(double Seconds, double Value);

/// <summary>Median, mean, minimum and maximum of a series (zeros when it is empty).</summary>
public sealed record MetricSummary(double Median, double Mean, double Min, double Max, int Count)
{
    public static MetricSummary Empty { get; } = new(0, 0, 0, 0, 0);

    public static MetricSummary Of(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).Order().ToArray();
        if (sorted.Length == 0)
        {
            return Empty;
        }

        var mid = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        return new MetricSummary(median, sorted.Average(), sorted[0], sorted[^1], sorted.Length);
    }

    public static MetricSummary Of(IEnumerable<NetSample> samples) => Of(samples.Select(s => s.Value));
}

/// <summary>Round-trip times of one phase (idle, during download, during upload) and their jitter.</summary>
public sealed record LatencyPhase(IReadOnlyList<NetSample> Samples)
{
    public MetricSummary Summary => MetricSummary.Of(Samples);

    /// <summary>Jitter as the mean absolute difference between consecutive RTTs (RFC 3550 style, without smoothing).</summary>
    public double Jitter => NetStats.Jitter(Samples.Select(s => s.Value).ToList());

    /// <summary>Jitter per sample (|RTT i − RTT i−1|), for the chart.</summary>
    public IReadOnlyList<NetSample> JitterSeries =>
        [.. Samples.Skip(1).Select((s, i) => new NetSample(s.Seconds, Math.Abs(s.Value - Samples[i].Value)))];
}

/// <summary>The Wi-Fi link of the adapter used for the test (Windows WLAN service).</summary>
public sealed record WifiLink(
    string? Ssid,
    int SignalPercent,
    int? RssiDbm,
    string? Standard,
    double? BandGhz,
    int? Channel,
    double? RxMbps,
    double? TxMbps);

/// <summary>The adapter that carries the default route: its name, kind, negotiated speed and, for Wi-Fi, the link.</summary>
public sealed record NetConnection(string Name, string Description, bool IsWifi, bool IsEthernet, double? LinkMbps, WifiLink? Wifi)
{
    /// <summary>Short text for tables and the CSV: "Wi-Fi 5 GHz −52 dBm", "Ethernet 1000 Mbps".</summary>
    public string Summary
    {
        get
        {
            if (IsWifi)
            {
                var parts = new List<string> { "Wi-Fi" };
                if (Wifi?.BandGhz is { } band)
                {
                    parts.Add($"{band.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} GHz");
                }

                if (Wifi?.RssiDbm is { } rssi)
                {
                    parts.Add($"{rssi} dBm");
                }
                else if (Wifi is not null)
                {
                    parts.Add($"{Wifi.SignalPercent}%");
                }

                return string.Join(" ", parts);
            }

            var kind = IsEthernet ? "Ethernet" : Name;
            return LinkMbps is { } mbps ? $"{kind} {mbps:0} Mbps" : kind;
        }
    }
}

/// <summary>Where the test ran: server label (data center or machine and city), provider and public address.</summary>
public sealed record NetEndpoint(string Host, string? Server, string? Provider, string? ClientAddress);

/// <summary>A complete Internet test.</summary>
public sealed record NetworkTestResult(
    string Version,
    NetServer Server,
    IpFamily Family,
    NetEndpoint Endpoint,
    IReadOnlyList<NetSample> Download,
    IReadOnlyList<NetSample> Upload,
    LatencyPhase Idle,
    LatencyPhase LoadedDownload,
    LatencyPhase LoadedUpload,
    double? LossPercent,
    NetConnection? Connection,
    DateTimeOffset At)
{
    public double DownloadMbps => MetricSummary.Of(Download).Median;

    public double UploadMbps => MetricSummary.Of(Upload).Median;
}

/// <summary>Small statistics shared by the test and its tests.</summary>
public static class NetStats
{
    public static double Jitter(IReadOnlyList<double> rtts)
    {
        ArgumentNullException.ThrowIfNull(rtts);
        if (rtts.Count < 2)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 1; i < rtts.Count; i++)
        {
            sum += Math.Abs(rtts[i] - rtts[i - 1]);
        }

        return sum / (rtts.Count - 1);
    }

    /// <summary>Packet loss in percent, or null when no probe got an answer although the connection works (ICMP blocked).</summary>
    public static double? Loss(int sent, int answered, bool connectionWorks)
    {
        if (sent <= 0 || (answered == 0 && connectionWorks))
        {
            return null;
        }

        return 100.0 * (sent - answered) / sent;
    }
}

/// <summary>A test that could not run (server unreachable, no IPv6, rate limited). The key is a string key.</summary>
public sealed class NetworkTestException : Exception
{
    public NetworkTestException()
    {
        Key = "net.error.unreachable";
    }

    public NetworkTestException(string key)
        : base(key)
    {
        Key = key;
    }

    public NetworkTestException(string key, Exception inner)
        : base(key, inner)
    {
        Key = key;
    }

    public string Key { get; }
}
