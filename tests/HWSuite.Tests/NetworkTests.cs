using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Network;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

public sealed class NetworkTests
{
    private static readonly NetTestOptions Fast = new()
    {
        IdleSamples = 6,
        IdleInterval = TimeSpan.FromMilliseconds(1),
        PhaseDuration = TimeSpan.FromMilliseconds(600),
        SampleInterval = TimeSpan.FromMilliseconds(50),
        LoadedRttInterval = TimeSpan.FromMilliseconds(20),
        EchoInterval = TimeSpan.FromMilliseconds(5),
    };

    /// <summary>A server that sends and receives at a fixed rate; latency rises while loaded.</summary>
    private sealed class FakeProbe : INetworkProbe
    {
        private volatile bool _loaded;
        private int _echoes;

        public long BytesPer10Ms { get; init; } = 125_000; // 100 Mbps

        public double IdleRtt { get; init; } = 5;

        public double LoadedRtt { get; init; } = 7;

        public bool? EchoReply { get; init; } = true;

        public int LoseEvery { get; init; }

        public bool FailDownload { get; init; }

        public NetEndpoint Endpoint { get; private set; } = new("", null, null, null);

        public Task PrepareAsync(CancellationToken cancel)
        {
            Endpoint = new NetEndpoint("test.example", "Test server", "Test ISP", "192.0.2.10");
            return Task.CompletedTask;
        }

        public Task<double?> RttAsync(CancellationToken cancel) => Task.FromResult<double?>(_loaded ? LoadedRtt : IdleRtt);

        public Task<bool?> EchoAsync(CancellationToken cancel)
        {
            if (EchoReply is null)
            {
                return Task.FromResult<bool?>(null);
            }

            var n = Interlocked.Increment(ref _echoes);
            return Task.FromResult<bool?>(LoseEvery > 0 && n % LoseEvery == 0 ? false : EchoReply);
        }

        public Task DownloadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel) =>
            FailDownload ? Task.FromException(new NetworkTestException("net.error.transfer")) : Pump(onBytes, cancel);

        public Task UploadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel) => Pump(onBytes, cancel);

        public void Dispose()
        {
        }

        private async Task Pump(Action<long> onBytes, CancellationToken cancel)
        {
            _loaded = true;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long sent = 0;
                while (!cancel.IsCancellationRequested)
                {
                    // Paced by elapsed time, so coarse timers do not change the rate.
                    var due = (long)(clock.Elapsed.TotalMilliseconds / 10 * BytesPer10Ms);
                    onBytes(due - sent);
                    sent = due;
                    await Task.Delay(5, cancel);
                }
            }
            finally
            {
                _loaded = false;
            }
        }
    }

    [Fact]
    public async Task Run_measures_throughput_latency_and_loss()
    {
        using var probe = new FakeProbe { LoseEvery = 10 };
        var phases = new List<string>();
        var progress = new SyncProgress(p => phases.Add(p.Phase));
        var result = await NetworkTest.RunAsync(probe, NetServer.Cloudflare, IpFamily.IPv4, null, Fast, progress);

        Assert.InRange(result.DownloadMbps, 40, 200);
        Assert.InRange(result.UploadMbps, 40, 200);
        Assert.NotEmpty(result.Download);
        Assert.Equal(5, result.Idle.Summary.Median);
        Assert.Equal(0, result.Idle.Jitter);
        Assert.Equal(7, result.LoadedDownload.Summary.Median);
        Assert.Equal(7, result.LoadedUpload.Summary.Median);
        Assert.NotNull(result.LossPercent);
        Assert.InRange(result.LossPercent!.Value, 3, 20);
        Assert.Equal("Test ISP", result.Endpoint.Provider);
        Assert.Equal(NetworkTest.Version, result.Version);
        Assert.Contains("prepare", phases);
        Assert.Contains("latency", phases);
        Assert.Contains("download", phases);
        Assert.Contains("upload", phases);
        Assert.Equal("done", phases[^1]);
    }

    [Fact]
    public async Task Run_reports_no_loss_when_echo_is_not_possible()
    {
        using var probe = new FakeProbe { EchoReply = null };
        var result = await NetworkTest.RunAsync(probe, NetServer.MLab, IpFamily.IPv6, null, Fast);
        Assert.Null(result.LossPercent);
        Assert.Equal(IpFamily.IPv6, result.Family);
    }

    [Fact]
    public async Task Run_fails_with_the_probe_error_when_no_data_moved()
    {
        using var probe = new FakeProbe { FailDownload = true };
        var ex = await Assert.ThrowsAsync<NetworkTestException>(() => NetworkTest.RunAsync(probe, NetServer.Cloudflare, IpFamily.IPv4, null, Fast));
        Assert.Equal("net.error.transfer", ex.Key);
    }

    [Fact]
    public async Task Run_can_be_cancelled()
    {
        using var probe = new FakeProbe();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkTest.RunAsync(probe, NetServer.Cloudflare, IpFamily.IPv4, null, Fast with { PhaseDuration = TimeSpan.FromSeconds(10) }, cancel: cancel.Token));
    }

    [Fact]
    public void Summary_and_jitter()
    {
        var odd = MetricSummary.Of([3.0, 1, 2]);
        Assert.Equal(2, odd.Median);
        Assert.Equal(2, odd.Mean);
        Assert.Equal(1, odd.Min);
        Assert.Equal(3, odd.Max);
        Assert.Equal(2.5, MetricSummary.Of([1.0, 2, 3, 4]).Median);
        Assert.Equal(MetricSummary.Empty, MetricSummary.Of(Array.Empty<double>()));
        Assert.Equal(2, NetStats.Jitter([5, 7, 5, 7]));
        Assert.Equal(0, NetStats.Jitter([5]));
        var phase = new LatencyPhase([new(0, 5), new(1, 8), new(2, 6)]);
        Assert.Equal(2.5, phase.Jitter);
        Assert.Equal([3.0, 2.0], phase.JitterSeries.Select(s => s.Value));
    }

    [Fact]
    public void Loss_is_unknown_when_echo_is_blocked()
    {
        Assert.Null(NetStats.Loss(40, 0, connectionWorks: true));
        Assert.Equal(100, NetStats.Loss(40, 0, connectionWorks: false));
        Assert.Equal(25, NetStats.Loss(40, 30, connectionWorks: true));
        Assert.Null(NetStats.Loss(0, 0, connectionWorks: true));
    }

    private static NetworkTestResult Result(
        double down = 900,
        double idle = 5,
        double loaded = 7,
        double? loss = 0,
        NetConnection? connection = null,
        double[]? idleSeries = null)
    {
        static List<NetSample> Flat(double v, int n = 10) => [.. Enumerable.Range(0, n).Select(i => new NetSample(i * 0.1, v))];
        var idleSamples = idleSeries is null ? Flat(idle) : [.. idleSeries.Select((v, i) => new NetSample(i * 0.1, v))];
        return new NetworkTestResult(
            NetworkTest.Version, NetServer.Cloudflare, IpFamily.IPv4,
            new NetEndpoint("speed.cloudflare.com", "Cloudflare São Paulo, BR (GRU)", "Neolink Telecom", "192.0.2.1"),
            Flat(down), Flat(600), new LatencyPhase(idleSamples), new LatencyPhase(Flat(loaded)), new LatencyPhase(Flat(loaded)),
            loss, connection, new DateTimeOffset(2026, 10, 28, 22, 21, 0, TimeSpan.FromHours(-3)));
    }

    private static NetConnection Wifi(int signal = 90, int? rssi = -50, double band = 5, double rx = 1200) =>
        new("Wi-Fi", "Intel Wi-Fi 6 AX200", true, false, rx, new WifiLink("Casa", signal, rssi, "802.11ax (Wi-Fi 6)", band, 36, rx, rx));

    private static NetConnection Ethernet(double link) => new("Ethernet", "Realtek PCIe 2.5GbE", false, true, link, null);

    [Fact]
    public void Advice_healthy_connection()
    {
        var tips = NetworkAdvice.For(Result(connection: Ethernet(2500)));
        Assert.Equal(["net.tip.healthy"], tips.Select(t => t.Key));
        Assert.Equal(AdviceLevel.Good, tips[0].Level);
    }

    [Fact]
    public void Advice_wifi_problems_come_first_with_wired_and_restart_suggestions()
    {
        var tips = NetworkAdvice.For(Result(down: 300, connection: Wifi(signal: 40, rssi: -75, band: 2.4, rx: 400)));
        var keys = tips.Select(t => t.Key).ToList();
        Assert.Equal(["net.tip.weakSignal", "net.tip.wifiRate", "net.tip.band24", "net.tip.wired", "net.tip.restart"], keys);
        Assert.Equal("-75 dBm", tips[0].Args[0]);
        Assert.Equal([400.0, 300.0], tips[1].Args.Cast<double>());
    }

    [Fact]
    public void Advice_uses_signal_percent_without_rssi()
    {
        Assert.Contains(NetworkAdvice.For(Result(down: 100, connection: Wifi(signal: 50, rssi: null))), t => t.Key == "net.tip.weakSignal" && (string)t.Args[0] == "50%");
        Assert.DoesNotContain(NetworkAdvice.For(Result(down: 100, connection: Wifi(signal: 80, rssi: null))), t => t.Key == "net.tip.weakSignal");
    }

    [Fact]
    public void Advice_cable_link()
    {
        Assert.Contains(NetworkAdvice.For(Result(down: 94, connection: Ethernet(100))), t => t.Key == "net.tip.slowLink");
        Assert.Contains(NetworkAdvice.For(Result(down: 930, connection: Ethernet(1000))), t => t.Key == "net.tip.linkLimit" && t.Level == AdviceLevel.Suggestion);
        Assert.DoesNotContain(NetworkAdvice.For(Result(down: 500, connection: Ethernet(1000))), t => t.Key == "net.tip.linkLimit");
    }

    [Fact]
    public void Advice_bufferbloat_loss_jitter_and_latency()
    {
        Assert.Contains(NetworkAdvice.For(Result(idle: 10, loaded: 80)), t => t.Key == "net.tip.bufferbloat" && (double)t.Args[1] == 80);
        Assert.DoesNotContain(NetworkAdvice.For(Result(idle: 10, loaded: 35)), t => t.Key == "net.tip.bufferbloat");
        Assert.Contains(NetworkAdvice.For(Result(loss: 2.5)), t => t.Key == "net.tip.loss");
        Assert.DoesNotContain(NetworkAdvice.For(Result(loss: null)), t => t.Key == "net.tip.loss");
        Assert.Contains(NetworkAdvice.For(Result(idleSeries: [5, 30, 5, 30, 5])), t => t.Key == "net.tip.jitter");
        Assert.Contains(NetworkAdvice.For(Result(idle: 90, loaded: 95)), t => t.Key == "net.tip.latency");
        Assert.DoesNotContain(NetworkAdvice.For(Result(loss: 2.5, connection: Ethernet(2500))), t => t.Key == "net.tip.wired");
    }

    [Fact]
    public void Every_advice_has_title_and_body_in_both_languages()
    {
        var keys = new[] { "weakSignal", "wifiRate", "band24", "slowLink", "linkLimit", "bufferbloat", "loss", "jitter", "latency", "wired", "restart", "healthy" };
        foreach (var language in new[] { "en", "pt-BR" })
        {
            var s = Strings.For(language);
            foreach (var key in keys)
            {
                Assert.NotEqual($"net.tip.{key}.title", s[$"net.tip.{key}.title"]);
                Assert.NotEqual($"net.tip.{key}.body", s[$"net.tip.{key}.body"]);
            }
        }
    }

    [Fact]
    public void History_round_trip_newest_first_without_ip_address()
    {
        var dir = Directory.CreateTempSubdirectory("hwsuite-net");
        try
        {
            var path = Path.Combine(dir.FullName, "net-benchmark.csv");
            var first = Result(down: 500, connection: Wifi());
            var second = Result(down: 909, loss: null, connection: Ethernet(1000)) with { At = first.At.AddHours(1), Family = IpFamily.IPv6 };
            NetworkHistory.Append(path, first);
            NetworkHistory.Append(path, second);
            File.AppendAllText(path, "garbage,line\n");

            var entries = NetworkHistory.Read(path);
            Assert.Equal(2, entries.Count);
            Assert.Equal(909, entries[0].DownloadMbps);
            Assert.Equal(IpFamily.IPv6, entries[0].Family);
            Assert.Null(entries[0].LossPercent);
            Assert.Equal("Ethernet 1000 Mbps", entries[0].Connection);
            Assert.Equal("Wi-Fi 5 GHz -50 dBm", entries[1].Connection);
            Assert.Equal("Cloudflare São Paulo, BR (GRU)", entries[1].ServerName);
            Assert.Equal("Neolink Telecom", entries[1].Provider);
            Assert.Equal(0, entries[1].LossPercent);
            Assert.Equal(first.At, entries[1].At);
            Assert.DoesNotContain("192.0.2.1", File.ReadAllText(path), StringComparison.Ordinal);

            Assert.Equal(BenchmarkKinds.Network, BenchmarkFiles.Clear(BenchmarkKinds.Network, dir.FullName));
            Assert.False(File.Exists(path));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Results_table_for_internet()
    {
        var s = Strings.For("pt-BR");
        var entries = new[] { NetHistoryEntry.From(Result(down: 450)), NetHistoryEntry.From(Result(down: 900, loss: null, connection: Ethernet(1000))) };
        var table = ResultTables.Network(entries, s);
        Assert.Equal(8, table.Columns.Count);
        Assert.Equal(2, table.BarColumn);
        Assert.Equal(0.5, table.Rows[0].Bar);
        Assert.True(table.Rows[1].IsBest);
        Assert.Equal("900", table.Rows[1].Cells[2]);
        Assert.Equal("5 / 7", table.Rows[1].Cells[4]);
        Assert.Equal("—", table.Rows[1].Cells[6]);
        Assert.Equal("0%", table.Rows[0].Cells[6]);
        Assert.Equal("Ethernet 1000 Mbps", table.Rows[1].Cells[7]);
        Assert.Contains("IPv4", table.Rows[0].Cells[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Cloudflare_meta()
    {
        var e = CloudflareProbe.ParseMeta("""{"hostname":"speed.cloudflare.com","clientIp":"177.128.253.99","httpProtocol":"HTTP/1.1","asn":262663,"asOrganization":"Neolink Telecom","colo":"GRU","country":"BR","city":"São Paulo","region":"Sao Paulo"}""");
        Assert.Equal("Cloudflare São Paulo, BR (GRU)", e.Server);
        Assert.Equal("Neolink Telecom", e.Provider);
        Assert.Equal("177.128.253.99", e.ClientAddress);
        Assert.Equal("Cloudflare", CloudflareProbe.ParseMeta("{}").Server);
        var nested = CloudflareProbe.ParseMeta("""{"clientIp":"2804:14c::1","asn":262663,"asOrganization":"Neolink Telecom","colo":{"iata":"GRU","lat":-23.4,"lon":-46.5,"cca2":"BR","region":"South America","city":"São Paulo"}}""");
        Assert.Equal("Cloudflare São Paulo, BR (GRU)", nested.Server);
        Assert.Equal("2804:14c::1", nested.ClientAddress);
        var trace = CloudflareProbe.ParseTrace("fl=1f\nh=speed.cloudflare.com\nip=177.128.253.99\nts=1.2\ncolo=GRU\nloc=BR\nwarp=off\n");
        Assert.Equal("Cloudflare BR (GRU)", trace.Server);
        Assert.Equal("177.128.253.99", trace.ClientAddress);
        Assert.Equal("Cloudflare", CloudflareProbe.ParseMeta("not json").Server);
    }

    [Fact]
    public void Mlab_locate_and_measurements()
    {
        const string json = """
            {"results":[{"machine":"mlab1-gru05.mlab-oti.measurement-lab.org","location":{"city":"São Paulo","country":"BR"},
            "urls":{"ws:///ndt/v7/download":"ws://x/ndt/v7/download?access_token=a","wss:///ndt/v7/download":"wss://ndt-mlab1-gru05.mlab-oti.measurement-lab.org/ndt/v7/download?access_token=d",
            "wss:///ndt/v7/upload":"wss://ndt-mlab1-gru05.mlab-oti.measurement-lab.org/ndt/v7/upload?access_token=u"}}]}
            """;
        var server = MlabProbe.ParseLocate(json);
        Assert.NotNull(server);
        Assert.Equal("São Paulo", server!.City);
        Assert.Equal("BR", server.Country);
        Assert.Equal("ndt-mlab1-gru05.mlab-oti.measurement-lab.org", server.Download.Host);
        Assert.Contains("access_token=u", server.Upload.Query, StringComparison.Ordinal);
        Assert.Equal("mlab1-gru05", MlabProbe.Site(server.Machine));
        Assert.Null(MlabProbe.ParseLocate("""{"results":[]}"""));
        Assert.Null(MlabProbe.ParseLocate("<html>"));

        Assert.Equal("177.128.253.99", MlabProbe.ClientAddress("""{"ConnectionInfo":{"Client":"177.128.253.99:51234","Server":"x:443"}}"""));
        Assert.Equal("2001:db8::1", MlabProbe.ClientAddress("""{"ConnectionInfo":{"Client":"[2001:db8::1]:51234"}}"""));
        Assert.Null(MlabProbe.ClientAddress("""{"AppInfo":{"NumBytes":1}}"""));
    }

    [Fact]
    public void Mlab_upload_message_size_scales_like_the_spec()
    {
        Assert.Equal(1 << 13, MlabProbe.NextMessageSize(1 << 13, 1 << 16)); // 8 KiB is not under 1/16 of 64 KiB
        Assert.Equal(1 << 14, MlabProbe.NextMessageSize(1 << 13, (1 << 17) + 16));
        Assert.Equal(1 << 22, MlabProbe.NextMessageSize(1 << 22, 1L << 40));
    }

    [Fact]
    public void Connection_summary()
    {
        Assert.Equal("Wi-Fi 5 GHz -50 dBm", Wifi().Summary);
        Assert.Equal("Wi-Fi 2.4 GHz 40%", Wifi(signal: 40, rssi: null, band: 2.4).Summary);
        Assert.Equal("Ethernet 2500 Mbps", Ethernet(2500).Summary);
    }

    [Theory]
    [InlineData(2412, 2.4, 1)]
    [InlineData(2437, 2.4, 6)]
    [InlineData(2484, 2.4, 14)]
    [InlineData(5180, 5.0, 36)]
    [InlineData(5745, 5.0, 149)]
    [InlineData(5955, 6.0, 1)]
    [InlineData(6115, 6.0, 33)]
    public void Wifi_band_and_channel(int mhz, double band, int channel)
    {
        Assert.Equal(band, WifiMath.Band(mhz));
        Assert.Equal(channel, WifiMath.Channel(mhz));
    }

    [Fact]
    public void Wifi_standard_and_rssi()
    {
        Assert.Equal("802.11ax (Wi-Fi 6)", WifiMath.Standard(10));
        Assert.Equal("802.11be (Wi-Fi 7)", WifiMath.Standard(11));
        Assert.Null(WifiMath.Standard(0));
        Assert.Equal(-50, WifiMath.RssiFromQuality(100));
        Assert.Equal(-100, WifiMath.RssiFromQuality(0));
        Assert.Equal(-70, WifiMath.RssiFromQuality(60));
    }

    [Fact]
    public void Wlan_connection_and_bss_layout()
    {
        byte[] bssid = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60];
        var connection = new byte[604];
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(0), 1); // connected
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(520), 4);
        Encoding.UTF8.GetBytes("Casa").CopyTo(connection, 524);
        bssid.CopyTo(connection, 560);
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(568), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(576), 78);
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(580), 1_200_900);
        BinaryPrimitives.WriteUInt32LittleEndian(connection.AsSpan(584), 960_700);

        var c = WlanReader.ParseConnection(connection);
        Assert.NotNull(c);
        Assert.Equal("Casa", c!.Ssid);
        Assert.Equal(bssid, c.Bssid);
        Assert.Equal(10, c.PhyType);
        Assert.Equal(78, c.SignalPercent);
        Assert.Equal(1_200_900, c.RxKbps);
        Assert.Equal(960_700, c.TxKbps);
        Assert.Null(WlanReader.ParseConnection(new byte[100]));

        var list = new byte[8 + (2 * 360)];
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(0), (uint)list.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(4), 2);
        new byte[] { 1, 2, 3, 4, 5, 6 }.CopyTo(list, 8 + 40);
        bssid.CopyTo(list, 8 + 360 + 40);
        BinaryPrimitives.WriteInt32LittleEndian(list.AsSpan(8 + 360 + 56), -58);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(8 + 360 + 92), 5_180_000);

        var bss = WlanReader.FindBss(list, bssid);
        Assert.NotNull(bss);
        Assert.Equal(-58, bss!.Rssi);
        Assert.Equal(5180, bss.FrequencyMhz);
        Assert.Null(WlanReader.FindBss(list, [9, 9, 9, 9, 9, 9]));
    }

    [Fact]
    public void Cli_net_options()
    {
        var o = HWSuite.Cli.CliOptions.Parse(["net", "--mlab", "--accept-mlab-policy", "--ipv6"]);
        Assert.Equal(HWSuite.Cli.CliCommand.Net, o.Command);
        Assert.True(o.Mlab && o.AcceptMlabPolicy && o.Ipv6);
        Assert.False(HWSuite.Cli.CliOptions.Parse(["net"]).Mlab);
        Assert.Throws<HWSuite.Cli.CliUsageException>(() => HWSuite.Cli.CliOptions.Parse(["bench", "--ipv6"]));
    }

    /// <summary>Reports synchronously (Progress&lt;T&gt; posts to the thread pool, so the order would not be kept).</summary>
    private sealed class SyncProgress(Action<NetProgress> report) : IProgress<NetProgress>
    {
        private readonly Lock _gate = new();

        public void Report(NetProgress value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
