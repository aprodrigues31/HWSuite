using System.Globalization;
using System.Text;
using HWSuite.Core.Model;

namespace HWSuite.Core.Spd;

/// <summary>
/// Decodes a memory module's SPD EEPROM (JEDEC JESD21-C Annex L for DDR4, JESD400-5 for DDR5)
/// and the Intel XMP blocks stored after it (XMP 2.0 on DDR4, XMP 3.0 on DDR5). Pure byte parsing,
/// so it is tested with dumps on any OS; reading the EEPROM over SMBus is the provider's job.
/// </summary>
public static class SpdDecoder
{
    public const byte TypeDdr4 = 0x0C;
    public const byte TypeDdr4E = 0x0E;
    public const byte TypeDdr5 = 0x12;

    /// <summary>Bytes to read for each memory type (DDR4: 512, DDR5: 1024); 0 when unsupported.</summary>
    public static int Length(byte memoryType) => memoryType switch
    {
        TypeDdr4 or TypeDdr4E => 512,
        TypeDdr5 => 1024,
        _ => 0,
    };

    /// <summary>Null when the dump is too short or is not DDR4/DDR5.</summary>
    public static SpdInfo? Decode(ReadOnlySpan<byte> spd, string? moduleManufacturer = null, string? dramManufacturer = null)
    {
        if (spd.Length < 3)
        {
            return null;
        }

        var info = spd[2] switch
        {
            TypeDdr4 or TypeDdr4E when spd.Length >= 512 => DecodeDdr4(spd),
            TypeDdr5 when spd.Length >= 1024 => DecodeDdr5(spd),
            _ => null,
        };

        if (info is not null)
        {
            info.ModuleManufacturer = Clean(moduleManufacturer);
            info.DramManufacturer = Clean(dramManufacturer);
        }

        return info;
    }

    // ---------------- DDR4 ----------------

    private const double Mtb = 0.125; // ns (byte 17 = 0: MTB 125 ps, FTB 1 ps)
    private const double Ftb = 0.001;

    private static SpdInfo DecodeDdr4(ReadOnlySpan<byte> s)
    {
        var info = new SpdInfo
        {
            MemoryType = "DDR4",
            SpdRevision = Revision(s[1]),
            ModuleType = ModuleType(s[3] & 0x0F),
            HasThermalSensor = (s[14] & 0x80) != 0,
            Ecc = ((s[13] >> 3) & 0x3) == 1,
            Ranks = ((s[12] >> 3) & 0x7) + 1,
            DeviceWidth = 4 << (s[12] & 0x7),
            DieDensityBits = Ddr4DieDensity(s[4] & 0x0F),
            PartNumber = Ascii(s.Slice(329, 20)),
            SerialNumber = Serial(s.Slice(325, 4)),
        };

        (info.ManufactureYear, info.ManufactureWeek) = Date(s[323], s[324]);

        var busWidth = 8 << (s[13] & 0x7);
        var dies = (s[6] & 0x80) != 0 ? ((s[6] >> 4) & 0x7) + 1 : 1;
        if (info.DieDensityBits is { } density && busWidth > 0)
        {
            info.SizeBytes = density / 8 * (busWidth / info.DeviceWidth.Value) * info.Ranks.Value * dies;
        }

        var tck = Ns(s[18], s[125]);
        if (tck > 0)
        {
            info.Jedec = Ddr4Profile("JEDEC", tck,
                tAa: Ns(s[24], s[123]),
                tRcd: Ns(s[25], s[122]),
                tRp: Ns(s[26], s[121]),
                tRas: (((s[27] & 0x0F) << 8) | s[28]) * Mtb,
                tRc: ((((s[27] >> 4) & 0x0F) << 8) | s[29]) * Mtb + ((sbyte)s[120] * Ftb),
                voltage: (s[11] & 0x1) != 0 ? 1.2 : null);
        }

        // Intel XMP 2.0: header at 384, profiles at 393 and 440 (47 bytes each).
        if (s[384] == 0x0C && s[385] == 0x4A)
        {
            info.XmpVersion = Revision(s[387]);
            ReadOnlySpan<int> bases = [393, 440];
            for (var p = 0; p < bases.Length; p++)
            {
                if ((s[386] & (1 << p)) == 0)
                {
                    continue; // profile not enabled by the vendor
                }

                var b = bases[p];
                var xtck = Ns(s[b + 3], s[b + 38]);
                if (xtck <= 0)
                {
                    continue;
                }

                var v = s[b];
                double? volts = (v >> 7) + ((v & 0x7F) / 100.0);
                if (volts is < 1.0 or > 2.0)
                {
                    volts = null;
                }

                info.XmpProfiles.Add(Ddr4Profile($"XMP {p + 1}", xtck,
                    tAa: Ns(s[b + 8], s[b + 37]),
                    tRcd: Ns(s[b + 9], s[b + 36]),
                    tRp: Ns(s[b + 10], s[b + 35]),
                    tRas: (((s[b + 11] & 0x0F) << 8) | s[b + 12]) * Mtb,
                    tRc: ((((s[b + 11] >> 4) & 0x0F) << 8) | s[b + 13]) * Mtb + ((sbyte)s[b + 34] * Ftb),
                    voltage: volts));
            }
        }

        return info;
    }

    private static SpdTimingProfile Ddr4Profile(string name, double tck, double tAa, double tRcd, double tRp, double tRas, double tRc, double? voltage) =>
        new(name, Speed(tck), Cycles(tAa, tck), Cycles(tRcd, tck), Cycles(tRp, tck), Cycles(tRas, tck), Cycles(tRc, tck), voltage);

    private static double Ns(byte mtb, byte fine) => (mtb * Mtb) + ((sbyte)fine * Ftb);

    /// <summary>DDR4 SDRAM density per die (byte 4 bits 3:0); 8 and 9 are the 12/24 Gb oddities.</summary>
    private static long? Ddr4DieDensity(int code) => code switch
    {
        <= 7 => (256L * 1024 * 1024) << code,
        8 => 12L * 1024 * 1024 * 1024,
        9 => 24L * 1024 * 1024 * 1024,
        _ => null,
    };

    // ---------------- DDR5 ----------------

    private static SpdInfo DecodeDdr5(ReadOnlySpan<byte> s)
    {
        var info = new SpdInfo
        {
            MemoryType = "DDR5",
            SpdRevision = Revision(s[1]),
            ModuleType = ModuleType(s[3] & 0x0F),
            HasThermalSensor = true, // every DDR5 module has an SPD hub with a sensor
            Ecc = ((s[235] >> 3) & 0x3) != 0,
            PartNumber = Ascii(s.Slice(521, 30)),
            SerialNumber = Serial(s.Slice(517, 4)),
        };

        (info.ManufactureYear, info.ManufactureWeek) = Date(s[515], s[516]);

        var tck = U16(s, 20);
        if (tck > 0)
        {
            info.Jedec = Ddr5Profile("JEDEC", tck, s, 30, voltage: 1.1);
        }

        // Intel XMP 3.0: header at 640, profiles of 64 bytes from 704.
        if (s[640] == 0x0C && s[641] == 0x4A)
        {
            info.XmpVersion = Revision(s[642]);
            for (var p = 0; p < 3; p++)
            {
                var b = 704 + (p * 64);
                var xtck = U16(s, b + 5);
                if (xtck < 100)
                {
                    continue; // empty or invalid profile slot
                }

                info.XmpProfiles.Add(Ddr5Profile($"XMP {p + 1}", xtck, s, b + 13, voltage: null));
            }
        }

        return info;
    }

    /// <summary>tAA, tRCD, tRP, tRAS, tRC as consecutive 16-bit picosecond values starting at <paramref name="first"/>.</summary>
    private static SpdTimingProfile Ddr5Profile(string name, int tckPs, ReadOnlySpan<byte> s, int first, double? voltage)
    {
        var tck = tckPs / 1000.0;
        var cl = Cycles(U16(s, first) / 1000.0, tck);
        cl += cl % 2; // DDR5 CAS latency is always even
        return new SpdTimingProfile(name, Speed(tck), cl,
            Cycles(U16(s, first + 2) / 1000.0, tck),
            Cycles(U16(s, first + 4) / 1000.0, tck),
            Cycles(U16(s, first + 6) / 1000.0, tck),
            Cycles(U16(s, first + 8) / 1000.0, tck),
            voltage);
    }

    private static int U16(ReadOnlySpan<byte> s, int offset) => s[offset] | (s[offset + 1] << 8);

    // ---------------- shared ----------------

    /// <summary>Clock cycles for a time, with JEDEC's rounding guard band (2.5 %).</summary>
    internal static int Cycles(double timeNs, double tckNs) =>
        timeNs <= 0 || tckNs <= 0 ? 0 : (int)Math.Ceiling((timeNs / tckNs) - 0.025);

    /// <summary>Data rate in MT/s from tCK, snapped to the 33/66/00 grid speeds are sold on (2666, 2933, 3200).</summary>
    internal static int Speed(double tckNs)
    {
        var rate = 2000.0 / tckNs;
        var snapped = Math.Round(rate / (100.0 / 3)) * (100.0 / 3);
        return (int)Math.Floor(snapped + 0.01);
    }

    private static string Revision(byte b) => string.Create(CultureInfo.InvariantCulture, $"{b >> 4}.{b & 0x0F}");

    private static string? ModuleType(int code) => code switch
    {
        1 => "RDIMM",
        2 => "UDIMM",
        3 => "SO-DIMM",
        4 => "LRDIMM",
        5 => "Mini-RDIMM",
        6 => "Mini-UDIMM",
        8 => "72b-SO-RDIMM",
        9 => "72b-SO-UDIMM",
        11 => "CAMM2",
        12 => "16b-SO-DIMM",
        13 => "32b-SO-DIMM",
        _ => null,
    };

    private static (int? Year, int? Week) Date(byte yearBcd, byte weekBcd)
    {
        var year = Bcd(yearBcd);
        var week = Bcd(weekBcd);
        return year is null || week is null or < 1 or > 53 ? (null, null) : (2000 + year, week);
    }

    private static int? Bcd(byte b) => (b >> 4) <= 9 && (b & 0x0F) <= 9 ? ((b >> 4) * 10) + (b & 0x0F) : null;

    private static string? Serial(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyExcept((byte)0x00) < 0 || bytes.IndexOfAnyExcept((byte)0xFF) < 0)
        {
            return null; // blank serial
        }

        return Convert.ToHexString(bytes);
    }

    private static string? Ascii(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is < 0x20 or > 0x7E)
            {
                break;
            }

            sb.Append((char)b);
        }

        return Clean(sb.ToString());
    }

    private static string? Clean(string? text)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) || t.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : t;
    }
}
