using HWSuite.Core.Network;

namespace HWSuite.Tests;

public sealed class WifiTests
{
    private static WifiBss N(string? ssid, int rssi, int mhz, int width = 20, bool me = false) =>
        new(ssid, "00:00:00:00:00:" + mhz.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)[^2..], rssi, mhz, WifiMath.Channel(mhz), WifiMath.Band(mhz), "802.11ax (Wi-Fi 6)", width, me);

    private static WifiStatus Status(IReadOnlyList<WifiBss> networks, int rssi = -50, double band = 2.4, int channel = 6, int width = 20, string? security = "WPA2-PSK", string? cipher = "AES (CCMP)", DateTime? driver = null) =>
        new("id", "Adapter", "1.0", driver ?? new DateTime(2026, 1, 1), "Maker", true, "Home", "AA", 90, rssi, "802.11ax (Wi-Fi 6)", band, channel, width, 600, 600, security, cipher, "192.168.0.1", networks, false);

    [Fact]
    public void Overlap_follows_the_band_rules()
    {
        Assert.Equal(1, WifiAnalysis.Overlap(2.4, 6, 20, 6));
        Assert.True(WifiAnalysis.Overlap(2.4, 6, 20, 8) > 0);
        Assert.Equal(0, WifiAnalysis.Overlap(2.4, 1, 20, 6));
        Assert.Equal(0, WifiAnalysis.Overlap(2.4, 1, 20, 11));
        Assert.Equal(1, WifiAnalysis.Overlap(5, 36, 80, 48));   // 36–48 block
        Assert.Equal(0, WifiAnalysis.Overlap(5, 36, 80, 52));
        Assert.Equal(1, WifiAnalysis.Overlap(5, 157, 80, 149));
        Assert.Equal(0, WifiAnalysis.Overlap(5, 36, 20, 40));
        Assert.True(WifiAnalysis.IsDfs(5, 100));
        Assert.False(WifiAnalysis.IsDfs(5, 36));
    }

    [Fact]
    public void Busy_channel_suggests_the_freest_one_and_ignores_own_access_points()
    {
        List<WifiBss> nets =
        [
            N("Home", -45, 2437, me: true),
            N("Home", -50, 2412), // own second AP: not a neighbour
            N("A", -55, 2437), N("B", -60, 2437), N("C", -62, 2442), N("D", -65, 2432),
            N("E", -85, 2412),
        ];
        var status = Status(nets);
        var loads = WifiAnalysis.Channels(nets, 2.4, "Home");
        Assert.Equal(11, WifiAnalysis.Best(loads)!.Channel);
        Assert.Equal(2, loads.Single(l => l.Channel == 1).Networks); // E and D; own AP on 1 not counted

        var advice = WifiAnalysis.Advice(status, null, new DateTime(2026, 11, 7));
        var tip = advice.Single(a => a.Key == "wifi.adv.channel");
        Assert.Equal(6, tip.Args[0]);
        Assert.Equal(11, tip.Args[2]);
        Assert.Contains(advice, a => a.Key == "wifi.adv.only24");
        Assert.DoesNotContain(advice, a => a.Key == "wifi.adv.legacy"); // 802.11ax is not 802.11a
    }

    [Fact]
    public void Advice_covers_signal_band_security_driver_and_stability()
    {
        var nets = new List<WifiBss> { N("Home", -78, 2437, me: true), N("Home", -60, 5180, 80) };
        var status = Status(nets, rssi: -78, security: "WPA-PSK", cipher: "TKIP", driver: new DateTime(2020, 5, 1));
        var samples = Enumerable.Range(0, 20).Select(i => new WifiSample(i, i % 2 == 0 ? -60 : -80, i < 10 ? "AA" : "BB", 100, i % 5 == 0 ? null : 30.0 + (i % 3 * 15), 40)).ToList();
        var stability = WifiStability.From(samples, "192.168.0.1", "1.1.1.1", TimeSpan.FromSeconds(20));
        Assert.Equal(1, stability.Roams);
        Assert.Equal(20, stability.Gateway.LossPercent);

        var keys = WifiAnalysis.Advice(status, stability, new DateTime(2026, 11, 7)).Select(a => a.Key).ToList();
        Assert.Contains("wifi.adv.weak", keys);
        Assert.Contains("wifi.adv.useFasterBand", keys);
        Assert.Contains("wifi.adv.security", keys);
        Assert.Contains("wifi.adv.driver", keys);
        Assert.Contains("wifi.adv.gwLoss", keys);
        Assert.Contains("wifi.adv.gwLatency", keys);
        Assert.Contains("wifi.adv.unstable", keys);
        Assert.DoesNotContain("wifi.adv.good", keys);
    }

    [Fact]
    public void Clean_wifi_is_reported_good_and_provider_problems_are_told_apart()
    {
        var nets = new List<WifiBss> { N("Home", -45, 5180, 80, me: true), N("X", -80, 5745, 80) };
        var status = Status(nets, rssi: -45, band: 5, channel: 36, width: 80);
        var good = WifiAnalysis.Advice(status, null, new DateTime(2026, 11, 7));
        Assert.Equal("wifi.adv.good", Assert.Single(good).Key);

        var samples = Enumerable.Range(0, 20).Select(i => new WifiSample(i, -45, "AA", 600, 2, i % 4 == 0 ? null : 20.0)).ToList();
        var st = WifiStability.From(samples, "gw", "net", TimeSpan.FromSeconds(20));
        var keys = WifiAnalysis.Advice(status, st, new DateTime(2026, 11, 7)).Select(a => a.Key).ToList();
        Assert.Contains("wifi.adv.isp", keys);
        Assert.DoesNotContain("wifi.adv.gwLoss", keys);
    }

    [Fact]
    public void Disconnection_in_the_test_counts_as_a_drop()
    {
        var samples = new List<WifiSample>
        {
            new(0, -50, "AA", 100, 2, 10), new(1, null, null, null, null, null), new(2, -50, "AA", 100, 2, 10),
        };
        var st = WifiStability.From(samples, "gw", "net", TimeSpan.FromSeconds(3));
        Assert.Equal(1, st.Drops);
        Assert.Equal(0, st.Roams);
    }
}
