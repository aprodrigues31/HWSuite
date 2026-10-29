namespace HWSuite.Core.Network;

/// <summary>How a tip reads: a problem found, a suggestion, or "all good".</summary>
public enum AdviceLevel
{
    Problem,
    Suggestion,
    Good,
}

/// <summary>
/// One improvement tip. <see cref="Key"/> names two strings, "Key.title" and "Key.body"; the body is formatted
/// with <see cref="Args"/> (the measured values that triggered it).
/// </summary>
public sealed record NetAdvice(string Key, AdviceLevel Level, params object[] Args);

/// <summary>
/// The "Improvements" tab: tips that follow from what was measured, problems first. Thresholds are the usual
/// ones for home connections (Wi-Fi signal under −67 dBm is weak for video calls, latency rising more than
/// 30 ms under load is bufferbloat, any loss over 1% is noticeable in games and calls).
/// </summary>
public static class NetworkAdvice
{
    public const int WeakRssi = -67;
    public const int WeakSignalPercent = 60;
    public const double BufferbloatMs = 30;
    public const double LossPercent = 1;
    public const double JitterMs = 10;
    public const double HighLatencyMs = 60;

    /// <summary>Real Wi-Fi throughput is roughly 60–70% of the link rate the adapter reports.</summary>
    public const double WifiEfficiency = 0.6;

    public static IReadOnlyList<NetAdvice> For(NetworkTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var tips = new List<NetAdvice>();
        var down = result.DownloadMbps;
        var c = result.Connection;
        var wifi = c?.Wifi;

        if (c is { IsWifi: true } && wifi is not null)
        {
            if (wifi.RssiDbm is { } rssi ? rssi < WeakRssi : wifi.SignalPercent < WeakSignalPercent)
            {
                tips.Add(new NetAdvice("net.tip.weakSignal", AdviceLevel.Problem, wifi.RssiDbm is { } r ? $"{r} dBm" : $"{wifi.SignalPercent}%"));
            }

            if (wifi.RxMbps is { } rx && rx > 0 && down >= rx * WifiEfficiency)
            {
                tips.Add(new NetAdvice("net.tip.wifiRate", AdviceLevel.Problem, Math.Round(rx), Math.Round(down)));
            }

            if (wifi.BandGhz is { } band && band < 3)
            {
                tips.Add(new NetAdvice("net.tip.band24", AdviceLevel.Suggestion));
            }
        }

        if (c is { IsEthernet: true, LinkMbps: { } link } && link > 0)
        {
            if (link <= 100)
            {
                tips.Add(new NetAdvice("net.tip.slowLink", AdviceLevel.Problem, Math.Round(link)));
            }
            else if (down >= link * 0.9)
            {
                tips.Add(new NetAdvice("net.tip.linkLimit", AdviceLevel.Suggestion, Math.Round(link)));
            }
        }

        var idle = result.Idle.Summary.Median;
        var loaded = Math.Max(result.LoadedDownload.Summary.Median, result.LoadedUpload.Summary.Median);
        if (idle > 0 && loaded - idle > BufferbloatMs)
        {
            tips.Add(new NetAdvice("net.tip.bufferbloat", AdviceLevel.Problem, Math.Round(idle), Math.Round(loaded)));
        }

        if (result.LossPercent is { } loss && loss > LossPercent)
        {
            tips.Add(new NetAdvice("net.tip.loss", AdviceLevel.Problem, Math.Round(loss, 1)));
        }

        if (result.Idle.Jitter > JitterMs)
        {
            tips.Add(new NetAdvice("net.tip.jitter", AdviceLevel.Problem, Math.Round(result.Idle.Jitter)));
        }

        if (idle > HighLatencyMs)
        {
            tips.Add(new NetAdvice("net.tip.latency", AdviceLevel.Problem, Math.Round(idle)));
        }

        var problems = tips.Any(t => t.Level == AdviceLevel.Problem);
        if (problems && c is { IsWifi: true })
        {
            tips.Add(new NetAdvice("net.tip.wired", AdviceLevel.Suggestion));
        }

        if (problems)
        {
            tips.Add(new NetAdvice("net.tip.restart", AdviceLevel.Suggestion));
        }

        if (tips.Count == 0)
        {
            tips.Add(new NetAdvice("net.tip.healthy", AdviceLevel.Good));
        }

        return [.. tips.OrderBy(t => t.Level)];
    }
}
