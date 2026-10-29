using System.Globalization;
using System.Text;
using HWSuite.Core.Benchmark;

namespace HWSuite.Core.Network;

/// <summary>A saved Internet test: the headline numbers (the per-100 ms series are only shown right after a test).</summary>
public sealed record NetHistoryEntry(
    DateTimeOffset At,
    string Version,
    NetServer Server,
    IpFamily Family,
    string? ServerName,
    string? Provider,
    double DownloadMbps,
    double UploadMbps,
    double LatencyMs,
    double LatencyDownloadMs,
    double LatencyUploadMs,
    double JitterMs,
    double JitterDownloadMs,
    double JitterUploadMs,
    double? LossPercent,
    string? Connection)
{
    public static NetHistoryEntry From(NetworkTestResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new NetHistoryEntry(
            r.At, r.Version, r.Server, r.Family, r.Endpoint.Server, r.Endpoint.Provider, r.DownloadMbps, r.UploadMbps,
            r.Idle.Summary.Median, r.LoadedDownload.Summary.Median, r.LoadedUpload.Summary.Median,
            r.Idle.Jitter, r.LoadedDownload.Jitter, r.LoadedUpload.Jitter, r.LossPercent, r.Connection?.Summary);
    }
}

/// <summary>Internet results kept in Documents\HWSuite\net-benchmark.csv (the public IP address is not saved).</summary>
public static class NetworkHistory
{
    private const string Header = "when,version,server,ip,server_name,provider,download_mbps,upload_mbps,latency_ms,latency_download_ms,latency_upload_ms,jitter_ms,jitter_download_ms,jitter_upload_ms,loss_pct,connection";
    private const int Fields = 16;

    public static void Append(string path, NetworkTestResult result) => Append(path, NetHistoryEntry.From(result));

    public static void Append(string path, NetHistoryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var c = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string N(double v) => v.ToString("0.##", c);
        var line = string.Join(",", new[]
        {
            e.At.ToString("yyyy-MM-ddTHH:mm:sszzz", c),
            e.Version,
            e.Server.ToString(),
            e.Family.ToString(),
            BenchmarkHistory.Quote(e.ServerName ?? ""),
            BenchmarkHistory.Quote(e.Provider ?? ""),
            N(e.DownloadMbps),
            N(e.UploadMbps),
            N(e.LatencyMs),
            N(e.LatencyDownloadMs),
            N(e.LatencyUploadMs),
            N(e.JitterMs),
            N(e.JitterDownloadMs),
            N(e.JitterUploadMs),
            e.LossPercent is { } loss ? N(loss) : "",
            BenchmarkHistory.Quote(e.Connection ?? ""),
        });
        File.AppendAllText(path, (File.Exists(path) ? "" : Header + "\n") + line + "\n", new UTF8Encoding(false));
    }

    /// <summary>Saved tests, newest first; malformed lines are skipped.</summary>
    public static IReadOnlyList<NetHistoryEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var c = CultureInfo.InvariantCulture;
        var entries = new List<NetHistoryEntry>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = BenchmarkHistory.Split(line);
            if (f.Count != Fields || !DateTimeOffset.TryParse(f[0], c, DateTimeStyles.None, out var at)
                || !Enum.TryParse<NetServer>(f[2], out var server) || !Enum.TryParse<IpFamily>(f[3], out var family))
            {
                continue;
            }

            var numbers = new double[8];
            var ok = true;
            for (var i = 0; i < numbers.Length; i++)
            {
                ok &= double.TryParse(f[6 + i], NumberStyles.Float, c, out numbers[i]);
            }

            if (!ok)
            {
                continue;
            }

            double? loss = double.TryParse(f[14], NumberStyles.Float, c, out var l) ? l : null;
            entries.Add(new NetHistoryEntry(
                at, f[1], server, family, Empty(f[4]), Empty(f[5]),
                numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[6], numbers[7],
                loss, Empty(f[15])));
        }

        entries.Reverse();
        return entries;
    }

    private static string? Empty(string s) => s.Length > 0 ? s : null;
}
