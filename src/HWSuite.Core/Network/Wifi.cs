using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace HWSuite.Core.Network;

/// <summary>One access point heard in a scan (an SSID can have several: dual-band routers, mesh nodes).</summary>
/// <param name="Bssid">"AA:BB:CC:DD:EE:FF".</param>
/// <param name="WidthMhz">Channel width announced by the access point (20, 40, 80, 160, 320).</param>
public sealed record WifiBss(
    string? Ssid,
    string Bssid,
    int Rssi,
    int FrequencyMhz,
    int? Channel,
    double? BandGhz,
    string? Standard,
    int WidthMhz,
    bool Connected);

/// <summary>The Wi-Fi adapter, its connection and what it hears around it.</summary>
public sealed record WifiStatus(
    string InterfaceId,
    string Adapter,
    string? DriverVersion,
    DateTime? DriverDate,
    string? DriverProvider,
    bool Connected,
    string? Ssid,
    string? Bssid,
    int SignalPercent,
    int? Rssi,
    string? Standard,
    double? BandGhz,
    int? Channel,
    int? WidthMhz,
    double? RxMbps,
    double? TxMbps,
    string? Security,
    string? Cipher,
    string? Gateway,
    IReadOnlyList<WifiBss> Networks,
    bool ScanBlocked)
{
    /// <summary>The access point this adapter is connected to, from the scan.</summary>
    public WifiBss? Current => Networks.FirstOrDefault(n => n.Connected);
}

/// <summary>How busy one channel is: networks heard on it (or overlapping it) weighted by how loud they are.</summary>
/// <param name="Networks">Other people's access points overlapping this channel.</param>
/// <param name="Score">Interference estimate: 1 per strong overlapping network (−70 dBm or louder), less for weaker ones.</param>
public sealed record ChannelLoad(double BandGhz, int Channel, int Networks, double Score, bool Dfs);

/// <summary>One second of the stability test.</summary>
public sealed record WifiSample(double Seconds, int? Rssi, string? Bssid, double? RxMbps, double? GatewayMs, double? InternetMs);

/// <summary>Round trips to one address: sent, lost, average, jitter (mean change between replies) and worst.</summary>
public sealed record PingStats(string Target, int Sent, int Lost, double? AverageMs, double? JitterMs, double? MaxMs)
{
    public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;

    public static PingStats From(string target, IEnumerable<double?> replies)
    {
        var all = replies.ToList();
        var ok = all.OfType<double>().ToList();
        double? jitter = ok.Count > 1 ? ok.Skip(1).Select((v, i) => Math.Abs(v - ok[i])).Average() : null;
        return new PingStats(target, all.Count, all.Count - ok.Count, ok.Count > 0 ? ok.Average() : null, jitter, ok.Count > 0 ? ok.Max() : null);
    }
}

/// <summary>What the stability test measured.</summary>
public sealed record WifiStability(
    TimeSpan Duration,
    PingStats Gateway,
    PingStats Internet,
    int? RssiMin,
    int? RssiMax,
    double? RssiAverage,
    int Roams,
    int Drops,
    IReadOnlyList<WifiSample> Samples)
{
    public static WifiStability From(IReadOnlyList<WifiSample> samples, string gateway, string internet, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var rssi = samples.Select(x => x.Rssi).OfType<int>().ToList();
        var roams = 0;
        var drops = 0;
        string? last = null;
        var wasConnected = true;
        foreach (var sample in samples)
        {
            var connected = sample.Bssid is not null;
            if (!connected && wasConnected)
            {
                drops++;
            }

            if (connected && last is not null && !string.Equals(last, sample.Bssid, StringComparison.OrdinalIgnoreCase))
            {
                roams++;
            }

            if (connected)
            {
                last = sample.Bssid;
            }

            wasConnected = connected;
        }

        return new WifiStability(
            duration,
            PingStats.From(gateway, samples.Select(x => x.GatewayMs)),
            PingStats.From(internet, samples.Select(x => x.InternetMs)),
            rssi.Count > 0 ? rssi.Min() : null,
            rssi.Count > 0 ? rssi.Max() : null,
            rssi.Count > 0 ? rssi.Average() : null,
            roams,
            drops,
            samples);
    }
}

/// <summary>
/// Wi-Fi analysis: which channels are crowded (by overlap with the networks heard), the best channel to move to,
/// and plain-language tips. Thresholds are the usual ones for home and office networks: −67 dBm is the minimum for
/// calls and video, −75 dBm is weak, more than 1% loss or 10 ms jitter to the router is a Wi-Fi problem.
/// </summary>
public static class WifiAnalysis
{
    public const int GoodRssi = -60;
    public const int FairRssi = -67;
    public const int WeakRssi = -75;
    public const double GatewayLossPercent = 1;
    public const double GatewayJitterMs = 10;
    public const double GatewayLatencyMs = 15;

    /// <summary>Channels worth using: 1/6/11 on 2.4 GHz, the non-DFS ones on 5 GHz, the 20 MHz channels on 6 GHz.</summary>
    public static IReadOnlyList<int> Candidates(double band) => band switch
    {
        2.4 => [1, 6, 11],
        5 => [36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 149, 153, 157, 161, 165],
        _ => [1, 5, 9, 13, 17, 21, 25, 29, 33, 37, 41, 45, 49, 53, 57, 61, 65, 69, 73, 77, 81, 85, 89, 93],
    };

    /// <summary>5 GHz channels 52–144 share the band with radars (DFS): the router may have to leave them.</summary>
    public static bool IsDfs(double band, int channel) => band == 5 && channel is >= 52 and <= 144;

    /// <summary>How loud a neighbour is, as interference weight.</summary>
    public static double Weight(int rssi) => rssi switch
    {
        >= -70 => 1,
        >= -80 => 0.5,
        _ => 0.2,
    };

    /// <summary>
    /// Whether a network on <paramref name="channel"/> with <paramref name="width"/> MHz overlaps channel
    /// <paramref name="target"/>, and how much (0..1). On 2.4 GHz channels are 5 MHz apart and a 20 MHz signal spills
    /// over its neighbours; on 5 and 6 GHz a wide channel takes a whole aligned block of 20 MHz channels.
    /// </summary>
    public static double Overlap(double band, int channel, int width, int target)
    {
        if (band == 2.4)
        {
            // A 40 MHz network uses its primary channel plus four channels up or down: treat its center as 2 away.
            var half = width >= 40 ? 6 : 4;
            var distance = Math.Abs(target - channel);
            return distance > half ? 0 : 1 - (distance / (double)(half + 1));
        }

        var step = Math.Max(1, Math.Max(20, width) / 5);
        var origin = band == 5 ? (channel >= 149 ? 149 : 36) : 1;
        var start = origin + ((channel - origin) / step * step);
        if (channel < origin)
        {
            start = channel;
        }

        return target >= start && target < start + step ? 1 : 0;
    }

    /// <summary>Load of each candidate channel of a band, counting everyone but the access points of the connected network.</summary>
    public static IReadOnlyList<ChannelLoad> Channels(IEnumerable<WifiBss> networks, double band, string? ownSsid)
    {
        ArgumentNullException.ThrowIfNull(networks);
        var others = networks
            .Where(n => n.BandGhz == band && n.Channel is not null)
            .Where(n => ownSsid is null || !string.Equals(n.Ssid, ownSsid, StringComparison.Ordinal))
            .ToList();
        return
        [
            .. Candidates(band).Select(ch =>
            {
                var hits = others.Select(n => (n, o: Overlap(band, n.Channel!.Value, n.WidthMhz, ch))).Where(x => x.o > 0).ToList();
                return new ChannelLoad(band, ch, hits.Count, Math.Round(hits.Sum(x => x.o * Weight(x.n.Rssi)), 2), IsDfs(band, ch));
            }),
        ];
    }

    /// <summary>The least loaded candidate (non-DFS preferred on 5 GHz when they tie).</summary>
    public static ChannelLoad? Best(IReadOnlyList<ChannelLoad> loads) =>
        loads.OrderBy(l => l.Score).ThenBy(l => l.Dfs).ThenBy(l => l.Channel).FirstOrDefault();

    /// <summary>The load where the connected access point sits (its primary channel).</summary>
    public static ChannelLoad? Own(WifiStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.BandGhz is not { } band || status.Channel is not { } channel)
        {
            return null;
        }

        var others = status.Networks
            .Where(n => n.BandGhz == band && n.Channel is not null && !string.Equals(n.Ssid, status.Ssid, StringComparison.Ordinal))
            .Select(n => (n, o: Overlap(band, n.Channel!.Value, n.WidthMhz, channel)))
            .Where(x => x.o > 0)
            .ToList();
        return new ChannelLoad(band, channel, others.Count, Math.Round(others.Sum(x => x.o * Weight(x.n.Rssi)), 2), IsDfs(band, channel));
    }

    /// <summary>Signal as a verdict word key: wifi.signal.excellent / good / fair / weak.</summary>
    public static string SignalKey(int rssi) => rssi switch
    {
        >= GoodRssi => "wifi.signal.excellent",
        >= FairRssi => "wifi.signal.good",
        >= WeakRssi => "wifi.signal.fair",
        _ => "wifi.signal.weak",
    };

    /// <summary>Tips, problems first, then suggestions; "all good" when nothing was found.</summary>
    public static IReadOnlyList<NetAdvice> Advice(WifiStatus status, WifiStability? stability, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(status);
        var tips = new List<NetAdvice>();
        if (!status.Connected)
        {
            tips.Add(new("wifi.adv.notConnected", AdviceLevel.Problem));
            return tips;
        }

        var rssi = status.Rssi ?? NetworkAdviceRssi(status.SignalPercent);
        if (rssi < WeakRssi)
        {
            tips.Add(new("wifi.adv.weak", AdviceLevel.Problem, rssi));
        }
        else if (rssi < FairRssi)
        {
            tips.Add(new("wifi.adv.fair", AdviceLevel.Suggestion, rssi));
        }

        // On 2.4 GHz while the same network is also heard on 5 or 6 GHz.
        if (status.BandGhz == 2.4)
        {
            var faster = status.Networks
                .Where(n => n.BandGhz is 5 or 6 && string.Equals(n.Ssid, status.Ssid, StringComparison.Ordinal))
                .OrderByDescending(n => n.Rssi)
                .FirstOrDefault();
            if (faster is not null && faster.Rssi >= WeakRssi)
            {
                tips.Add(new("wifi.adv.useFasterBand", AdviceLevel.Suggestion, faster.BandGhz!.Value, faster.Rssi));
            }
            else if (!status.Networks.Any(n => n.BandGhz is 5 or 6 && n.Ssid is not null && n.Ssid.Length > 0 && string.Equals(n.Ssid, status.Ssid, StringComparison.Ordinal)))
            {
                tips.Add(new("wifi.adv.only24", AdviceLevel.Suggestion));
            }
        }

        // Crowded channel and a clearly better one.
        if (status.BandGhz is { } band && Own(status) is { } own)
        {
            var loads = Channels(status.Networks, band, status.Ssid);
            if (Best(loads) is { } best && best.Channel != own.Channel && own.Score >= 2 && own.Score - best.Score >= 1.5)
            {
                tips.Add(new("wifi.adv.channel", own.Score >= 4 ? AdviceLevel.Problem : AdviceLevel.Suggestion, own.Channel, own.Networks, best.Channel, best.Networks));
            }

            if (band == 2.4 && status.WidthMhz >= 40 && loads.Sum(l => l.Networks) >= 6)
            {
                tips.Add(new("wifi.adv.width24", AdviceLevel.Suggestion, status.WidthMhz.Value));
            }

            if (IsDfs(band, own.Channel))
            {
                tips.Add(new("wifi.adv.dfs", AdviceLevel.Suggestion, own.Channel));
            }
        }

        // Old standard or slow link.
        if (status.Standard is "802.11a" or "802.11b" or "802.11g" && status.Standard is { } std)
        {
            tips.Add(new("wifi.adv.legacy", AdviceLevel.Problem, std));
        }
        else if (status.RxMbps is { } rate && rate < (status.BandGhz == 2.4 ? 72 : 200) && rssi >= FairRssi)
        {
            tips.Add(new("wifi.adv.lowRate", AdviceLevel.Suggestion, Math.Round(rate)));
        }

        // Security.
        if (status.Security is { } sec && (sec is "Open" or "WEP" or "WPA" or "WPA-PSK" || status.Cipher is "TKIP" or "WEP"))
        {
            tips.Add(new("wifi.adv.security", AdviceLevel.Problem, status.Cipher is "TKIP" or "WEP" ? $"{sec} / {status.Cipher}" : sec));
        }

        // Old driver.
        if (status.DriverDate is { } date && (today - date).TotalDays > 2 * 365)
        {
            tips.Add(new("wifi.adv.driver", AdviceLevel.Suggestion, date.Year));
        }

        if (stability is { } st)
        {
            var gw = st.Gateway;
            if (st.Drops > 0)
            {
                tips.Add(new("wifi.adv.drops", AdviceLevel.Problem, st.Drops));
            }

            if (gw.LossPercent > GatewayLossPercent)
            {
                tips.Add(new("wifi.adv.gwLoss", AdviceLevel.Problem, Math.Round(gw.LossPercent, 1)));
            }

            if (gw.JitterMs > GatewayJitterMs || gw.AverageMs > GatewayLatencyMs)
            {
                tips.Add(new("wifi.adv.gwLatency", AdviceLevel.Problem, Math.Round(gw.AverageMs ?? 0, 1), Math.Round(gw.JitterMs ?? 0, 1)));
            }

            var wifiFine = gw.LossPercent <= GatewayLossPercent && !(gw.JitterMs > GatewayJitterMs);
            if (wifiFine && (st.Internet.LossPercent > GatewayLossPercent || st.Internet.JitterMs > 2 * GatewayJitterMs))
            {
                tips.Add(new("wifi.adv.isp", AdviceLevel.Suggestion, Math.Round(st.Internet.LossPercent, 1), Math.Round(st.Internet.JitterMs ?? 0, 1)));
            }

            if (st.Roams >= 3)
            {
                tips.Add(new("wifi.adv.roaming", AdviceLevel.Suggestion, st.Roams));
            }

            if (st.RssiMax - st.RssiMin >= 15)
            {
                tips.Add(new("wifi.adv.unstable", AdviceLevel.Suggestion, st.RssiMin!.Value, st.RssiMax!.Value));
            }
        }

        if (status.ScanBlocked)
        {
            tips.Add(new("wifi.adv.location", AdviceLevel.Suggestion));
        }

        if (!tips.Any(t => t.Level == AdviceLevel.Problem) && tips.All(t => t.Key == "wifi.adv.location"))
        {
            tips.Insert(0, new("wifi.adv.good", AdviceLevel.Good, rssi));
        }

        return [.. tips.OrderBy(t => t.Level)];
    }

    private static int NetworkAdviceRssi(int percent) => WifiMath.RssiFromQuality(percent);

    // ---- Stability test ---------------------------------------------------------------------------------------

    /// <summary>
    /// Once a second for <paramref name="duration"/>: the Wi-Fi link (<paramref name="link"/>: RSSI, BSSID and rate,
    /// or null when disconnected) and one ping each to the router and to the Internet, run together.
    /// </summary>
    public static async Task<WifiStability> RunStabilityAsync(
        Func<(int? Rssi, string? Bssid, double? RxMbps)> link,
        string gateway,
        string internet,
        TimeSpan duration,
        IProgress<WifiSample>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        var samples = new List<WifiSample>();
        using var gwPing = new Ping();
        using var netPing = new Ping();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < duration && !cancel.IsCancellationRequested)
        {
            var tick = watch.Elapsed;
            var gwTask = PingOnce(gwPing, gateway);
            var netTask = PingOnce(netPing, internet);
            var l = link();
            await Task.WhenAll(gwTask, netTask).ConfigureAwait(false);
            var sample = new WifiSample(Math.Round(tick.TotalSeconds, 1), l.Rssi, l.Bssid, l.RxMbps, gwTask.Result, netTask.Result);
            samples.Add(sample);
            progress?.Report(sample);
            var wait = TimeSpan.FromSeconds(1) - (watch.Elapsed - tick);
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

        return WifiStability.From(samples, gateway, internet, watch.Elapsed);
    }

    private static async Task<double?> PingOnce(Ping ping, string host)
    {
        try
        {
            var reply = await ping.SendPingAsync(host, 900).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
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

    /// <summary>The default gateway (IPv4) of the adapter with this id, or null.</summary>
    public static string? GatewayOf(string interfaceId)
    {
        try
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Id.Trim('{', '}'), interfaceId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
            return adapter?.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}

/// <summary>The last Wi-Fi test, kept for the maintenance report.</summary>
public sealed record WifiSummary(
    DateTimeOffset At,
    string? Ssid,
    int? Rssi,
    double? BandGhz,
    int? Channel,
    string? Standard,
    double? GatewayLossPercent,
    double? GatewayJitterMs,
    int Problems,
    int Suggestions)
{
    public static WifiSummary From(WifiStatus status, WifiStability? stability, IReadOnlyList<NetAdvice> advice, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(advice);
        return new WifiSummary(at, status.Ssid, status.Rssi, status.BandGhz, status.Channel, status.Standard,
            stability?.Gateway.LossPercent, stability?.Gateway.JitterMs,
            advice.Count(a => a.Level == AdviceLevel.Problem), advice.Count(a => a.Level == AdviceLevel.Suggestion));
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Save(string path, WifiSummary summary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(summary, Json));
    }

    public static WifiSummary? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WifiSummary>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"Casa-5G · 5 GHz ch 36 · −52 dBm".</summary>
    public string Text(CultureInfo culture) =>
        string.Join(" · ", new[]
        {
            Ssid,
            BandGhz is { } b ? $"{b.ToString("0.#", culture)} GHz" + (Channel is { } ch ? $" ch {ch}" : "") : null,
            Rssi is { } r ? $"{r} dBm" : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
}
