using System.Text;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Core.Spd;

namespace HWSuite.Tests;

public class SpdTests
{
    /// <summary>
    /// DDR4 UDIMM, 16 GB, 2 ranks of x8 chips of 8 Gb, JEDEC DDR4-3200 22-22-22, with an XMP 2.0
    /// profile DDR4-3200 16-18-18-36 at 1.35 V. Built per JESD21-C Annex L and the XMP 2.0 layout.
    /// </summary>
    internal static byte[] Ddr4(bool xmp = true, byte[]? serial = null)
    {
        var s = new byte[512];
        s[0] = 0x23;
        s[1] = 0x11; // SPD revision 1.1
        s[2] = SpdDecoder.TypeDdr4;
        s[3] = 0x02; // UDIMM
        s[4] = 0x45; // 8 Gb per die, 4 bank groups
        s[6] = 0x00; // monolithic
        s[11] = 0x03; // 1.2 V operable
        s[12] = 0x09; // 2 ranks, x8
        s[13] = 0x03; // 64-bit, no ECC
        s[14] = 0x00; // no thermal sensor
        s[18] = 5; // tCK 0.625 ns
        s[24] = 110; // tAA 13.75 ns
        s[25] = 110;
        s[26] = 110;
        s[27] = 0x11; // tRAS and tRC upper nibbles
        s[28] = 0x00; // tRAS 256 MTB = 32 ns
        s[29] = 0x6E; // tRC 366 MTB = 45.75 ns

        s[322] = 0x01;
        s[323] = 0x23; // 2023
        s[324] = 0x12; // week 12
        (serial ?? [0xE6, 0xA1, 0xB2, 0xC3]).CopyTo(s, 325);
        Encoding.ASCII.GetBytes("CT16G4DFRA32A".PadRight(20)).CopyTo(s, 329);

        if (xmp)
        {
            s[384] = 0x0C;
            s[385] = 0x4A;
            s[386] = 0x01; // profile 1 enabled
            s[387] = 0x20; // XMP 2.0
            const int b = 393;
            s[b] = 0xA3; // 1.35 V
            s[b + 3] = 5; // tCK 0.625 ns
            s[b + 8] = 80; // tAA 10 ns
            s[b + 9] = 90; // tRCD 11.25 ns
            s[b + 10] = 90; // tRP 11.25 ns
            s[b + 11] = 0x10; // tRC upper nibble 1, tRAS upper nibble 0
            s[b + 12] = 180; // tRAS 22.5 ns
            s[b + 13] = 14; // tRC 270 MTB = 33.75 ns
        }

        return s;
    }

    private static void U16(byte[] s, int offset, int value)
    {
        s[offset] = (byte)(value & 0xFF);
        s[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>DDR5 UDIMM, JEDEC DDR5-4800 40-39-39, XMP 3.0 profile DDR5-6000 30-38-38-78.</summary>
    internal static byte[] Ddr5()
    {
        var s = new byte[1024];
        s[1] = 0x10;
        s[2] = SpdDecoder.TypeDdr5;
        s[3] = 0x02;
        U16(s, 20, 416);
        U16(s, 30, 16000);
        U16(s, 32, 16000);
        U16(s, 34, 16000);
        U16(s, 36, 32000);
        U16(s, 38, 48000);
        s[515] = 0x24;
        s[516] = 0x07;
        new byte[] { 0x01, 0x02, 0x03, 0x04 }.CopyTo(s, 517);
        Encoding.ASCII.GetBytes("F5-6000J3038F16G".PadRight(30)).CopyTo(s, 521);

        s[640] = 0x0C;
        s[641] = 0x4A;
        s[642] = 0x30;
        const int b = 704;
        U16(s, b + 5, 333);
        U16(s, b + 13, 9990);
        U16(s, b + 15, 12654);
        U16(s, b + 17, 12654);
        U16(s, b + 19, 25974);
        U16(s, b + 21, 38628);
        return s;
    }

    [Fact]
    public void Ddr4_module_identity_and_organization()
    {
        var spd = SpdDecoder.Decode(Ddr4(), "Micron Technology", "Micron Technology")!;
        Assert.Equal("DDR4", spd.MemoryType);
        Assert.Equal("1.1", spd.SpdRevision);
        Assert.Equal("UDIMM", spd.ModuleType);
        Assert.Equal("CT16G4DFRA32A", spd.PartNumber);
        Assert.Equal("E6A1B2C3", spd.SerialNumber);
        Assert.Equal(2023, spd.ManufactureYear);
        Assert.Equal(12, spd.ManufactureWeek);
        Assert.Equal(2, spd.Ranks);
        Assert.Equal(8, spd.DeviceWidth);
        Assert.Equal(8L * 1024 * 1024 * 1024, spd.DieDensityBits);
        Assert.Equal(16L * 1024 * 1024 * 1024, spd.SizeBytes);
        Assert.False(spd.Ecc);
        Assert.False(spd.HasThermalSensor);
        Assert.Equal("Micron Technology", spd.DramManufacturer);
    }

    [Fact]
    public void Ddr4_jedec_and_xmp_profiles()
    {
        var spd = SpdDecoder.Decode(Ddr4())!;
        Assert.Equal(new SpdTimingProfile("JEDEC", 3200, 22, 22, 22, 52, 74, 1.2), spd.Jedec);
        Assert.Equal("2.0", spd.XmpVersion);
        var xmp = Assert.Single(spd.XmpProfiles);
        Assert.Equal(new SpdTimingProfile("XMP 1", 3200, 16, 18, 18, 36, 54, 1.35), xmp);
    }

    [Fact]
    public void Ddr4_without_xmp_has_only_jedec()
    {
        var spd = SpdDecoder.Decode(Ddr4(xmp: false))!;
        Assert.Null(spd.XmpVersion);
        Assert.Empty(spd.XmpProfiles);
        Assert.NotNull(spd.Jedec);
    }

    [Fact]
    public void Ddr5_jedec_and_xmp3()
    {
        var spd = SpdDecoder.Decode(Ddr5(), "G.Skill", "SK Hynix")!;
        Assert.Equal("DDR5", spd.MemoryType);
        Assert.Equal("F5-6000J3038F16G", spd.PartNumber);
        Assert.Equal("01020304", spd.SerialNumber);
        Assert.Equal(2024, spd.ManufactureYear);
        Assert.True(spd.HasThermalSensor);
        Assert.Equal(new SpdTimingProfile("JEDEC", 4800, 40, 39, 39, 77, 116, 1.1), spd.Jedec);
        Assert.Equal("3.0", spd.XmpVersion);
        Assert.Equal(new SpdTimingProfile("XMP 1", 6000, 30, 38, 38, 78, 116, null), Assert.Single(spd.XmpProfiles));
    }

    [Theory]
    [InlineData(0.625, 3200)]
    [InlineData(0.75, 2666)]
    [InlineData(0.833, 2400)]
    [InlineData(0.682, 2933)]
    [InlineData(0.416, 4800)]
    public void Speed_snaps_to_the_sold_grade(double tck, int expected)
    {
        Assert.Equal(expected, SpdDecoder.Speed(tck));
    }

    [Fact]
    public void Unsupported_or_short_dumps_are_ignored()
    {
        Assert.Null(SpdDecoder.Decode([0x92, 0x13, 0x0B])); // DDR3
        Assert.Null(SpdDecoder.Decode(Ddr4().AsSpan(0, 256)));
        Assert.Equal(0, SpdDecoder.Length(0x0B));
        Assert.Equal(512, SpdDecoder.Length(SpdDecoder.TypeDdr4));
        Assert.Equal(1024, SpdDecoder.Length(SpdDecoder.TypeDdr5));
    }

    [Fact]
    public void Blank_serial_and_date_are_dropped()
    {
        var bytes = Ddr4(serial: [0xFF, 0xFF, 0xFF, 0xFF]);
        bytes[323] = 0x00;
        bytes[324] = 0x00;
        var spd = SpdDecoder.Decode(bytes)!;
        Assert.Null(spd.SerialNumber);
        Assert.Null(spd.ManufactureYear);
    }

    [Fact]
    public void Spd_attaches_by_serial_then_by_order()
    {
        var memory = new MemoryInfo();
        memory.Modules.Add(new MemoryModule { Slot = "DIMM_A2", SerialNumber = "0E6A1B2C4", SizeBytes = 1 });
        memory.Modules.Add(new MemoryModule { Slot = "DIMM_B2", SerialNumber = "E6A1B2C3", SizeBytes = 1 });

        var first = SpdDecoder.Decode(Ddr4())!; // serial E6A1B2C3 -> DIMM_B2
        var second = SpdDecoder.Decode(Ddr4(serial: [0xAA, 0xBB, 0xCC, 0xDD]))!; // no match -> remaining slot
        Assert.Equal(2, SpdMerger.Attach(memory, [first, second]));
        Assert.Same(first, memory.Modules[1].Spd);
        Assert.Same(second, memory.Modules[0].Spd);
        Assert.Equal("CT16G4DFRA32A", memory.Modules[0].PartNumber); // filled from SPD
    }

    [Fact]
    public void Spd_creates_modules_when_smbios_has_none()
    {
        var memory = new MemoryInfo();
        Assert.Equal(1, SpdMerger.Attach(memory, [SpdDecoder.Decode(Ddr4())!]));
        var module = Assert.Single(memory.Modules);
        Assert.Equal(16L * 1024 * 1024 * 1024, module.SizeBytes);
        Assert.Equal("DDR4", module.Type);
    }

    [Fact]
    public void Report_shows_chip_maker_date_and_profiles()
    {
        var inventory = new HardwareInventory { Memory = new MemoryInfo() };
        inventory.Memory.Modules.Add(new MemoryModule { Slot = "DIMM_A2", SizeBytes = 16L << 30, Spd = SpdDecoder.Decode(Ddr4(), null, "Micron Technology") });
        var section = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Memória");
        var rows = section.Groups.Single(g => g.Title == "DIMM_A2").Rows.ToDictionary(r => r.Label, r => r.Value);

        Assert.Equal("Micron Technology", rows["Fabricante dos chips"]);
        Assert.Equal("semana 12 de 2023", rows["Fabricação"]);
        Assert.Equal("2 rank(s), chips x8 de 8 Gb", rows["Organização"]);
        Assert.Equal("Não", rows["Sensor de temperatura"]);
        Assert.Equal("DDR4-3200 22-22-22-52 · 1,20 V", rows["JEDEC"]);
        Assert.Equal("DDR4-3200 16-18-18-36 · 1,35 V", rows["XMP 2.0 perfil 1"]);
    }

    /// <summary>
    /// Crucial Ballistix BL16G30C15U4W.M16FE1 as CPU-Z 3.01 reads it on the user's PC: JEDEC #10
    /// DDR4-2666 (1333 MHz) and "XMP-3002" (1501 MHz) 15-16-16-35 tRC 69 at 1.35 V, week 05/2020.
    /// </summary>
    [Fact]
    public void Matches_cpuz_on_a_crucial_ballistix_module()
    {
        var s = Ddr4(serial: [0xE4, 0x28, 0xD2, 0xC8]);
        s[18] = 6; // tCK 0.75 ns: DDR4-2666
        s[24] = s[25] = s[26] = 114; // 14.25 ns
        s[27] = 0x11;
        s[28] = 0x00; // tRAS 32 ns
        s[29] = 0x72; // tRC 370 MTB = 46.25 ns
        s[323] = 0x20;
        s[324] = 0x05;
        Array.Fill(s, (byte)0x20, 329, 20);
        Encoding.ASCII.GetBytes("BL16G30C15U4W.M16FE1").CopyTo(s, 329);

        const int b = 393;
        s[b + 3] = 5;
        s[b + 38] = 41; // tCK 0.625 + 0.041 = 0.666 ns ("3002")
        s[b + 8] = 80; // tAA 10 ns
        s[b + 9] = s[b + 10] = 85; // 10.625 ns
        s[b + 11] = 0x10;
        s[b + 12] = 186; // tRAS 23.25 ns
        s[b + 13] = 0x6F; // tRC 367 MTB = 45.875 ns

        var spd = SpdDecoder.Decode(s, "Crucial Technology", "Micron Technology")!;
        Assert.Equal("BL16G30C15U4W.M16FE1", spd.PartNumber);
        Assert.Equal("E428D2C8", spd.SerialNumber);
        Assert.Equal((2020, 5), (spd.ManufactureYear!.Value, spd.ManufactureWeek!.Value));
        Assert.Equal(new SpdTimingProfile("JEDEC", 2666, 19, 19, 19, 43, 62, 1.2), spd.Jedec);
        Assert.Equal(new SpdTimingProfile("XMP 1", 3000, 15, 16, 16, 35, 69, 1.35), Assert.Single(spd.XmpProfiles));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reading_only_the_needed_ranges_decodes_the_same(bool ddr5)
    {
        var full = ddr5 ? Ddr5() : Ddr4();
        var partial = new byte[full.Length];
        var ranges = SpdDecoder.RangesToRead(full[2], i => full[i]);
        foreach (var (from, to) in ranges)
        {
            Array.Copy(full, from, partial, from, to - from + 1);
        }

        var expected = SpdDecoder.Decode(full)!;
        var actual = SpdDecoder.Decode(partial)!;
        Assert.Equal(expected.Jedec, actual.Jedec);
        Assert.True(expected.XmpProfiles.SequenceEqual(actual.XmpProfiles));
        Assert.True(actual.XmpProfiles.Count > 0);
        Assert.Equal(expected.PartNumber, actual.PartNumber);
        Assert.Equal(expected.SerialNumber, actual.SerialNumber);
        Assert.Equal(expected.ManufactureYear, actual.ManufactureYear);
        Assert.Equal(expected.Ranks, actual.Ranks);
        Assert.Equal(expected.SizeBytes, actual.SizeBytes);
        Assert.Equal(expected.Ecc, actual.Ecc);
        Assert.True(ranges.Sum(r => r.To - r.From + 1) < (ddr5 ? 360 : 200));
    }

    [Fact]
    public void Xmp_block_is_skipped_when_absent()
    {
        var bytes = Ddr4(xmp: false);
        Assert.False(SpdDecoder.RangesToRead(bytes[2], i => bytes[i]).Any(r => r.From == 393));
    }
}
