using System.Buffers.Binary;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Core.Storage;

namespace HWSuite.Tests;

public class AtaSmartTests
{
    private sealed record Attr(byte Id, byte Current, byte Worst, long Raw, byte Threshold);

    /// <summary>A SATA SSD as most vendors report it (Samsung/Crucial style ids).</summary>
    private static readonly Attr[] TypicalSsd =
    [
        new(0x05, 100, 100, 0, 10), // reallocated sectors
        new(0x09, 99, 99, 12_345, 0), // power-on hours
        new(0x0C, 99, 99, 1_500, 0), // power cycles
        new(0xB1, 95, 95, 30, 0), // 177 wear leveling: 95 % life left
        new(0xB3, 100, 100, 0, 10), // 179 used reserved blocks
        new(0xBB, 100, 100, 0, 0), // 187 uncorrectable
        new(0xC2, 67, 52, 0x002D_0021, 0), // 194 temperature: 33 °C now (min/max in the upper bytes)
        new(0xC7, 100, 100, 0, 0), // 199 CRC errors
        new(0xF1, 99, 99, 20_000_000_000, 0), // 241 LBAs written
    ];

    private static (byte[] Data, byte[] Thresholds) Pages(IEnumerable<Attr> attributes)
    {
        var data = new byte[512];
        var limits = new byte[512];
        data[0] = 0x10;
        limits[0] = 0x10;
        var i = 0;
        foreach (var a in attributes)
        {
            var e = data.AsSpan(2 + (i * 12), 12);
            e[0] = a.Id;
            e[1] = 0x33;
            e[3] = a.Current;
            e[4] = a.Worst;
            BinaryPrimitives.WriteUInt32LittleEndian(e[5..], (uint)(a.Raw & 0xFFFFFFFF));
            BinaryPrimitives.WriteUInt16LittleEndian(e[9..], (ushort)(a.Raw >> 32));
            var t = limits.AsSpan(2 + (i * 12), 12);
            t[0] = a.Id;
            t[1] = a.Threshold;
            i++;
        }

        Checksum(data);
        Checksum(limits);
        return (data, limits);
    }

    private static void Checksum(byte[] page)
    {
        byte sum = 0;
        for (var k = 0; k < 511; k++)
        {
            sum += page[k];
        }

        page[511] = (byte)(0x100 - sum);
    }

    [Fact]
    public void Pages_are_parsed_with_thresholds()
    {
        var (data, limits) = Pages(TypicalSsd);
        Assert.True(AtaSmart.IsValidPage(data));
        Assert.True(AtaSmart.IsValidPage(limits));

        var attributes = AtaSmart.Parse(data, limits);
        Assert.Equal(TypicalSsd.Length, attributes.Count);
        Assert.Equal(new SmartEntry(5, 100, 100, 10, 0), attributes[0]);
        Assert.Equal(new SmartEntry(0xF1, 99, 99, 0, 20_000_000_000), attributes[^1]);
    }

    [Fact]
    public void Junk_pages_are_rejected()
    {
        var (data, _) = Pages(TypicalSsd);
        data[100] ^= 0xFF;
        Assert.False(AtaSmart.IsValidPage(data));
        Assert.False(AtaSmart.IsValidPage(new byte[512])); // bridge returned zeros
        Assert.False(AtaSmart.IsValidPage(new byte[100]));
    }

    [Fact]
    public void Summary_of_a_healthy_ssd()
    {
        var (data, limits) = Pages(TypicalSsd);
        var health = AtaSmart.Summarize(AtaSmart.Parse(data, limits), ssd: true);

        Assert.Equal(DiskHealthStatus.Good, health.Assessment);
        Assert.Equal(33, health.TemperatureCelsius);
        Assert.Equal(12_345, health.PowerOnHours);
        Assert.Equal(1_500, health.PowerCycles);
        Assert.Equal(5, health.PercentageUsed);
        Assert.Equal(0, health.ReallocatedSectors);
        Assert.Equal(20_000_000_000L * 512, health.DataWrittenBytes);
        Assert.Null(health.DataReadBytes);
    }

    [Fact]
    public void Reallocated_sectors_mean_caution_and_a_threshold_hit_means_bad()
    {
        var caution = TypicalSsd.Select(a => a.Id == 0x05 ? a with { Raw = 8 } : a);
        var (data, limits) = Pages(caution);
        Assert.Equal(DiskHealthStatus.Caution, AtaSmart.Assess(AtaSmart.Parse(data, limits)));

        var pending = TypicalSsd.Append(new Attr(0xC5, 100, 100, 3, 0));
        (data, limits) = Pages(pending);
        var health = AtaSmart.Summarize(AtaSmart.Parse(data, limits), ssd: false);
        Assert.Equal(DiskHealthStatus.Caution, health.Assessment);
        Assert.Equal(3, health.PendingSectors);
        Assert.Null(health.PercentageUsed); // HDD: no wear estimate

        var failing = TypicalSsd.Select(a => a.Id == 0x05 ? a with { Current = 9 } : a);
        (data, limits) = Pages(failing);
        var attributes = AtaSmart.Parse(data, limits);
        Assert.Equal(DiskHealthStatus.Bad, AtaSmart.Assess(attributes));
        Assert.True(attributes[0].BelowThreshold);
    }

    [Fact]
    public void Power_on_hours_ignore_vendor_bytes_above_32_bits()
    {
        var (data, limits) = Pages([new Attr(0x09, 100, 100, (5L << 32) | 1000, 0)]);
        Assert.Equal(1000, AtaSmart.Summarize(AtaSmart.Parse(data, limits), ssd: false).PowerOnHours);
    }

    [Theory]
    [InlineData(0, 5, 100, DiskHealthStatus.Good)]
    [InlineData(1, 5, 100, DiskHealthStatus.Bad)]
    [InlineData(0, 92, 100, DiskHealthStatus.Caution)]
    [InlineData(0, 5, 5, DiskHealthStatus.Caution)]
    public void Nvme_assessment(int criticalWarning, int used, int spare, DiskHealthStatus expected)
    {
        var health = new DiskHealth { CriticalWarning = criticalWarning, PercentageUsed = used, AvailableSparePercent = spare };
        Assert.Equal(expected, AtaSmart.AssessNvme(health));
    }

    [Fact]
    public void Smart_receive_request_layout()
    {
        var b = AtaCommands.SendCmdIn(AtaCommands.SmartReadData, driveNumber: 3);
        Assert.Equal(32, b.Length);
        Assert.Equal(512, BinaryPrimitives.ReadInt32LittleEndian(b));
        Assert.True(b.AsSpan(4, 7).SequenceEqual(new byte[] { 0xD0, 1, 1, 0x4F, 0xC2, 0xB0, 0xB0 })); // drive 3 -> slave bit set
        Assert.Equal(3, b[12]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sat_pass_through_layout(bool sixteen)
    {
        var b = AtaCommands.SatPassThrough(AtaCommands.SmartReadThresholds, sixteen);
        Assert.Equal(600, b.Length);
        Assert.Equal(56, BinaryPrimitives.ReadUInt16LittleEndian(b));
        Assert.Equal(sixteen ? 16 : 12, b[6]);
        Assert.Equal(1, b[8]); // data in
        Assert.Equal(512u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12)));
        Assert.Equal(88ul, BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(24)));
        Assert.Equal(56u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(32)));
        var cdb = b.AsSpan(36, 16).ToArray();
        Assert.Equal(sixteen ? 0x85 : 0xA1, cdb[0]);
        Assert.Equal(0x08, cdb[1]); // PIO data-in
        Assert.Equal(0x0E, cdb[2]);
        Assert.Contains((byte)0xD1, cdb);
        Assert.Equal(0xB0, sixteen ? cdb[14] : cdb[9]);
    }

    [Fact]
    public void Report_shows_health_and_the_attribute_table()
    {
        var (data, limits) = Pages(TypicalSsd.Select(a => a.Id == 0x05 ? a with { Raw = 8 } : a));
        var inventory = new HardwareInventory();
        inventory.Disks.Add(new DiskInfo
        {
            Index = 1,
            Model = "Samsung SSD 860 EVO 500GB",
            BusType = "SATA",
            Health = AtaSmart.Summarize(AtaSmart.Parse(data, limits), ssd: true),
        });

        var sections = ReportModel.Build(inventory, Strings.For("pt-BR"));
        var disk = sections.Single(x => x.Title == "Armazenamento").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Atenção", disk["Saúde"]);
        Assert.Equal("8 / —", disk["Setores realocados / pendentes"]); // no pending-sector attribute on this drive

        var smart = sections.Single(x => x.Title == "S.M.A.R.T.").Groups.Single();
        Assert.Equal("#1 Samsung SSD 860 EVO 500GB", smart.Title);
        var row = smart.Rows[0];
        Assert.Equal("05 Setores realocados", row.Label);
        Assert.Equal("atual 100 · pior 100 · limite 10 · bruto 8", row.Value);
        Assert.Contains(smart.Rows, r => r.Label == "C2 Temperatura");
    }
}
