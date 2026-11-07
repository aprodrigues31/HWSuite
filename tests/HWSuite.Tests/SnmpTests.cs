using System.Text;
using HWSuite.Core.Snmp;

namespace HWSuite.Tests;

public sealed class SnmpTests
{
    [Fact]
    public void Ber_round_trips_oids_and_integers()
    {
        foreach (var oid in new[] { "1.3.6.1.2.1.1.5.0", "1.3.6.1.4.1.11863.6.1.1.2", "2.999.3", "1.3.6.1.2.1.31.1.1.1.6.4294967295" })
        {
            Assert.Equal(oid, Ber.ReadOid(Ber.OidBytes(oid)));
        }

        foreach (var n in new long[] { 0, 1, 127, 128, 255, 256, -1, -129, int.MaxValue, int.MinValue })
        {
            var tlv = Ber.Number(n);
            var (tag, start, length) = Ber.Read(tlv, 0);
            Assert.Equal(SnmpTag.Number, tag);
            Assert.Equal(n, Ber.ReadSigned(tlv.AsSpan(start, length)));
        }

        var big = Ber.Tlv(SnmpTag.OctetString, new byte[300]);
        Assert.Equal(300, Ber.Read(big, 0).Length);
        Assert.Equal(18446744073709551615UL, new SnmpValue(SnmpTag.Counter64, [0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]).AsUnsigned());
        Assert.Equal("AA:BB:CC:00:11:22", Ber.Text(new byte[] { 0xAA, 0xBB, 0xCC, 0, 0x11, 0x22 }));
        Assert.Equal("ER605", Ber.Text(Encoding.UTF8.GetBytes("ER605\0")));
    }

    [Fact]
    public void Keys_match_rfc_3414_appendix_a3()
    {
        var engine = Convert.FromHexString("000000000000000000000002");
        var md5 = Usm.PasswordToKey(SnmpAuth.Md5, "maplesyrup");
        Assert.Equal("9FAF3283884E92834EBC9847D8EDD963", Convert.ToHexString(md5));
        Assert.Equal("526F5EED9FCCE26F8964C2930787D82B", Convert.ToHexString(Usm.Localize(SnmpAuth.Md5, md5, engine)));
        var sha = Usm.PasswordToKey(SnmpAuth.Sha1, "maplesyrup");
        Assert.Equal("9FB5CC0381497B3793528939FF788D5D79145211", Convert.ToHexString(sha));
        Assert.Equal("6695FEBC9288E36282235FC7151F128497B38F3F", Convert.ToHexString(Usm.Localize(SnmpAuth.Sha1, sha, engine)));
    }

    [Fact]
    public void Sha224_matches_the_standard_vectors()
    {
        Assert.Equal("23097D223405D8228642A477BDA255B32AADBCE4BDA0B3F7E36C9DA7", Convert.ToHexString(Usm.Sha224(Encoding.ASCII.GetBytes("abc"))));
        Assert.Equal("D14A028C2A3A2BC9476102BB288234C415A2B01F828EA62AC5B3E42F", Convert.ToHexString(Usm.Sha224([])));
        Assert.Equal("75388B16512776CC5DBA5DA1FD890150B0C6455CB4F58B1952522525",
            Convert.ToHexString(Usm.Sha224(Encoding.ASCII.GetBytes("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"))));
    }

    [Fact]
    public void Aes_and_des_privacy_round_trip()
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var plain = Encoding.ASCII.GetBytes("scoped PDU of an odd length!!");
        foreach (var priv in new[] { SnmpPriv.Des, SnmpPriv.Aes128, SnmpPriv.Aes192, SnmpPriv.Aes256 })
        {
            var cipher = Usm.Encrypt(priv, key, plain, 3, 1234, 42, out var salt);
            var back = Usm.Decrypt(priv, key, cipher, 3, 1234, salt);
            Assert.Equal(plain, back[..plain.Length]);
        }
    }

    private static SnmpPoll Poll(DateTimeOffset at, params SnmpPort[] ports) =>
        new(at, new SnmpSystem("ER605", "ER605", null, TimeSpan.FromHours(1), null, null, null, null), ports);

    private static SnmpPort Port(int i, string name, bool up, double speed, ulong inOct, ulong outOct, bool hc = true, ulong errors = 0) =>
        new(i, name, null, name, 6, true, up, speed, inOct, outOct, hc, errors, 0, 0, 0);

    [Fact]
    public void Rates_handle_32bit_wrap_and_skip_resets()
    {
        var t0 = DateTimeOffset.Now;
        var a = Poll(t0, Port(1, "WAN1", true, 1000, 4294967000UL, 0, hc: false), Port(2, "LAN1", true, 1000, 5_000_000, 0));
        var b = Poll(t0.AddSeconds(10), Port(1, "WAN1", true, 1000, 1_249_704UL, 0, hc: false), Port(2, "LAN1", true, 1000, 100, 0));
        var rates = SnmpReader.Rates(a, b).ToDictionary(r => r.Index);
        Assert.Equal(1.0, rates[1].InMbps, 3);   // (1 250 000 bytes over the wrap) * 8 / 10 s
        Assert.Equal(0.0, rates[2].InMbps, 3);   // 64-bit counter went back: a reset, not traffic
    }

    [Fact]
    public void Changes_report_link_speed_errors_and_restart()
    {
        var t0 = DateTimeOffset.Now;
        var a = Poll(t0, Port(1, "WAN1", true, 1000, 0, 0), Port(2, "LAN2", true, 1000, 0, 0), Port(3, "LAN3", false, 0, 0, 0));
        var b = new SnmpPoll(t0.AddSeconds(10), a.System with { Uptime = TimeSpan.FromMinutes(1) },
            [Port(1, "WAN1", false, 1000, 0, 0), Port(2, "LAN2", true, 100, 0, 0, errors: 25), Port(3, "LAN3", true, 1000, 0, 0)]);
        var kinds = SnmpReader.Changes("d", a, b).Select(e => (e.Kind, e.Subject)).ToList();
        Assert.Contains((SnmpEventKind.Restarted, "ER605"), kinds);
        Assert.Contains((SnmpEventKind.PortDown, "WAN1"), kinds);
        Assert.Contains((SnmpEventKind.SpeedChanged, "LAN2"), kinds);
        Assert.Contains((SnmpEventKind.Errors, "LAN2"), kinds);
        Assert.Contains((SnmpEventKind.PortUp, "LAN3"), kinds);
        Assert.True(Port(1, "WAN1 (Xturbo)", true, 1, 0, 0).IsWan);
        Assert.False(Port(1, "LAN1", true, 1, 0, 0).IsWan);
    }
}
