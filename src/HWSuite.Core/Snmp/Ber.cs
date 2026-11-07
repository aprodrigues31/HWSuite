using System.Globalization;
using System.Numerics;
using System.Text;

namespace HWSuite.Core.Snmp;

/// <summary>ASN.1 BER tags used by SNMP (RFC 3416).</summary>
public static class SnmpTag
{
    public const byte Number = 0x02;
    public const byte OctetString = 0x04;
    public const byte Null = 0x05;
    public const byte Oid = 0x06;
    public const byte Sequence = 0x30;
    public const byte IpAddress = 0x40;
    public const byte Counter32 = 0x41;
    public const byte Gauge32 = 0x42;
    public const byte TimeTicks = 0x43;
    public const byte Opaque = 0x44;
    public const byte Counter64 = 0x46;
    public const byte NoSuchObject = 0x80;
    public const byte NoSuchInstance = 0x81;
    public const byte EndOfMibView = 0x82;
    public const byte GetRequest = 0xA0;
    public const byte GetNextRequest = 0xA1;
    public const byte Response = 0xA2;
    public const byte GetBulkRequest = 0xA5;
    public const byte Report = 0xA8;
}

/// <summary>One value of a variable binding: its tag and its raw content bytes.</summary>
public sealed record SnmpValue(byte Tag, byte[] Raw)
{
    public static readonly SnmpValue Null = new(SnmpTag.Null, []);

    public bool IsException => Tag is SnmpTag.NoSuchObject or SnmpTag.NoSuchInstance or SnmpTag.EndOfMibView;

    /// <summary>Integer, counters, gauges and time ticks as a number (unsigned types never negative).</summary>
    public long? AsLong() => Tag switch
    {
        SnmpTag.Number => (long)Ber.ReadSigned(Raw),
        SnmpTag.Counter32 or SnmpTag.Gauge32 or SnmpTag.TimeTicks => (long)Ber.ReadUnsigned(Raw),
        SnmpTag.Counter64 => (long)Math.Min(Ber.ReadUnsigned(Raw), long.MaxValue),
        _ => null,
    };

    /// <summary>Counter64 in full.</summary>
    public ulong? AsUnsigned() => Tag is SnmpTag.Counter32 or SnmpTag.Gauge32 or SnmpTag.TimeTicks or SnmpTag.Counter64 ? Ber.ReadUnsigned(Raw) : null;

    /// <summary>Readable text: strings as UTF-8 (hex when not printable), OIDs dotted, IP addresses, numbers.</summary>
    public override string ToString() => Tag switch
    {
        SnmpTag.OctetString => Ber.Text(Raw),
        SnmpTag.Oid => Ber.ReadOid(Raw),
        SnmpTag.IpAddress when Raw.Length == 4 => string.Join('.', Raw),
        SnmpTag.Null => "",
        SnmpTag.NoSuchObject => "noSuchObject",
        SnmpTag.NoSuchInstance => "noSuchInstance",
        SnmpTag.EndOfMibView => "endOfMibView",
        _ when AsUnsigned() is { } u => u.ToString(CultureInfo.InvariantCulture),
        _ when AsLong() is { } l => l.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToHexString(Raw),
    };

    public static SnmpValue FromInt(long value) => new(SnmpTag.Number, Ber.SignedBytes(value));

    public static SnmpValue Octets(byte[] value) => new(SnmpTag.OctetString, value);
}

/// <summary>An OID and its value.</summary>
public sealed record Varbind(string Oid, SnmpValue Value);

/// <summary>Basic Encoding Rules, only what SNMP needs.</summary>
public static class Ber
{
    public static byte[] Tlv(byte tag, ReadOnlySpan<byte> content)
    {
        var length = Length(content.Length);
        var result = new byte[1 + length.Length + content.Length];
        result[0] = tag;
        length.CopyTo(result, 1);
        content.CopyTo(result.AsSpan(1 + length.Length));
        return result;
    }

    public static byte[] Sequence(byte tag, params byte[][] items) => Tlv(tag, Concat(items));

    public static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, at);
            at += part.Length;
        }

        return result;
    }

    public static byte[] Number(long value) => Tlv(SnmpTag.Number, SignedBytes(value));

    public static byte[] Octets(ReadOnlySpan<byte> value) => Tlv(SnmpTag.OctetString, value);

    public static byte[] Octets(string value) => Octets(Encoding.UTF8.GetBytes(value));

    public static byte[] NullValue() => [SnmpTag.Null, 0];

    public static byte[] Oid(string oid) => Tlv(SnmpTag.Oid, OidBytes(oid));

    public static byte[] Encode(SnmpValue value) => Tlv(value.Tag, value.Raw);

    public static byte[] Length(int length)
    {
        if (length < 0x80)
        {
            return [(byte)length];
        }

        var bytes = new List<byte>();
        for (var v = length; v > 0; v >>= 8)
        {
            bytes.Insert(0, (byte)(v & 0xFF));
        }

        bytes.Insert(0, (byte)(0x80 | bytes.Count));
        return [.. bytes];
    }

    public static byte[] SignedBytes(long value)
    {
        var bytes = new BigInteger(value).ToByteArray(isUnsigned: false, isBigEndian: true);
        return bytes.Length == 0 ? [0] : bytes;
    }

    public static long ReadSigned(ReadOnlySpan<byte> raw) =>
        raw.Length == 0 ? 0 : (long)new BigInteger(raw, isUnsigned: false, isBigEndian: true);

    public static ulong ReadUnsigned(ReadOnlySpan<byte> raw)
    {
        ulong value = 0;
        foreach (var b in raw)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    public static byte[] OidBytes(string oid)
    {
        var arcs = oid.Trim().TrimStart('.').Split('.').Select(a => ulong.Parse(a, CultureInfo.InvariantCulture)).ToArray();
        if (arcs.Length < 2)
        {
            throw new FormatException($"OID too short: {oid}");
        }

        var bytes = new List<byte>();
        void Arc(ulong v)
        {
            var chunk = new List<byte> { (byte)(v & 0x7F) };
            for (v >>= 7; v > 0; v >>= 7)
            {
                chunk.Insert(0, (byte)(0x80 | (v & 0x7F)));
            }

            bytes.AddRange(chunk);
        }

        Arc((arcs[0] * 40) + arcs[1]);
        foreach (var arc in arcs.Skip(2))
        {
            Arc(arc);
        }

        return [.. bytes];
    }

    public static string ReadOid(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
        {
            return "";
        }

        var arcs = new List<ulong>();
        ulong value = 0;
        var first = true;
        foreach (var b in raw)
        {
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                if (first)
                {
                    var a = Math.Min(2, value / 40);
                    arcs.Add(a);
                    arcs.Add(value - (a * 40));
                    first = false;
                }
                else
                {
                    arcs.Add(value);
                }

                value = 0;
            }
        }

        return string.Join('.', arcs);
    }

    /// <summary>A DisplayString as text, or "0x…" when it is binary (MAC addresses, engine ids).</summary>
    public static string Text(ReadOnlySpan<byte> raw)
    {
        var bytes = raw.ToArray();
        string? decoded;
        try
        {
            decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimEnd('\0');
        }
        catch (DecoderFallbackException)
        {
            decoded = null;
        }

        if (decoded is not null && decoded.All(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t'))
        {
            return decoded.TrimEnd(' ', '\r', '\n');
        }

        return bytes.Length == 6
            ? string.Join(':', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
            : "0x" + Convert.ToHexString(bytes);
    }

    /// <summary>Reads one TLV at <paramref name="at"/>: its tag, where the content starts and how long it is.</summary>
    public static (byte Tag, int Start, int Length) Read(ReadOnlySpan<byte> data, int at)
    {
        if (at + 2 > data.Length)
        {
            throw new FormatException("Truncated BER");
        }

        var tag = data[at];
        int length = data[at + 1];
        var start = at + 2;
        if ((length & 0x80) != 0)
        {
            var count = length & 0x7F;
            if (count is 0 or > 4 || start + count > data.Length)
            {
                throw new FormatException("Bad BER length");
            }

            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | data[start + i];
            }

            start += count;
        }

        if (length < 0 || start + length > data.Length)
        {
            throw new FormatException("Truncated BER");
        }

        return (tag, start, length);
    }

    /// <summary>The children of a constructed value, as (tag, start, length) in <paramref name="data"/>.</summary>
    public static List<(byte Tag, int Start, int Length)> Children(ReadOnlySpan<byte> data, int start, int length)
    {
        var list = new List<(byte, int, int)>();
        var at = start;
        while (at < start + length)
        {
            var item = Read(data, at);
            list.Add(item);
            at = item.Start + item.Length;
        }

        return list;
    }
}
