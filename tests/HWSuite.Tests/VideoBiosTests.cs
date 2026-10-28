using System.Buffers.Binary;
using System.Text;
using HWSuite.Cli;
using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class VideoBiosTests
{
    /// <summary>A small ATOM option ROM: 55 AA, length in 512-byte blocks, date at 0x50, ATOM header, part number, checksum.</summary>
    private static byte[] Rom(int blocks = 2, bool fixChecksum = true)
    {
        var rom = new byte[blocks * 512];
        rom[0] = 0x55;
        rom[1] = 0xAA;
        rom[2] = (byte)blocks;
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x48), 0x100);
        Encoding.ASCII.GetBytes("2023/07/13 01:28").CopyTo(rom, 0x50);
        Encoding.ASCII.GetBytes("ATOM").CopyTo(rom, 0x104);
        Encoding.ASCII.GetBytes("113-APM6766SL-100\0NAVI32 XL").CopyTo(rom, 0x200);
        if (fixChecksum)
        {
            byte sum = 0;
            for (var i = 0; i < rom.Length - 1; i++)
            {
                sum += rom[i];
            }

            rom[^1] = (byte)(0x100 - sum);
        }

        return rom;
    }

    /// <summary>ACPI header (36) + UUID (16) + VBIOS offset + Lib1 offset + reserved (16), then image header (28) + image.</summary>
    private static byte[] Vfct(params byte[][] roms)
    {
        const int Start = 76;
        var table = new byte[Start + roms.Sum(r => 28 + r.Length)];
        Encoding.ASCII.GetBytes("VFCT").CopyTo(table, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), (uint)table.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(52), Start);
        var offset = Start;
        foreach (var rom in roms)
        {
            var h = table.AsSpan(offset, 28);
            BinaryPrimitives.WriteUInt32LittleEndian(h, 3); // bus
            BinaryPrimitives.WriteUInt16LittleEndian(h[12..], 0x1002);
            BinaryPrimitives.WriteUInt16LittleEndian(h[14..], 0x747E);
            BinaryPrimitives.WriteUInt16LittleEndian(h[16..], 0x1849);
            BinaryPrimitives.WriteUInt16LittleEndian(h[18..], 0x5327);
            BinaryPrimitives.WriteUInt32LittleEndian(h[24..], (uint)rom.Length);
            rom.CopyTo(table, offset + 28);
            offset += 28 + rom.Length;
        }

        return table;
    }

    [Fact]
    public void Vfct_images_are_extracted_with_their_pci_ids()
    {
        var rom = Rom();
        var image = Assert.Single(VideoBios.ParseVfct(Vfct(rom)));
        Assert.Equal((3, 0x1002, 0x747E, 0x1849, 0x5327), (image.Bus, (int)image.VendorId, (int)image.DeviceId, (int)image.SubsystemVendorId, (int)image.SubsystemId));
        Assert.True(rom.AsSpan().SequenceEqual(image.Rom));
        Assert.Equal(2, VideoBios.ParseVfct(Vfct(Rom(), Rom(4))).Count);
    }

    [Fact]
    public void Truncated_or_foreign_tables_give_nothing()
    {
        var table = Vfct(Rom());
        Assert.Empty(VideoBios.ParseVfct(table.AsSpan(0, 120)));
        table[0] = (byte)'X';
        Assert.Empty(VideoBios.ParseVfct(table));
        Assert.Empty(VideoBios.ParseVfct([]));
    }

    [Fact]
    public void Rom_is_checked_and_described()
    {
        var copy = VideoBios.Describe(Rom(), "ACPI VFCT")!;
        Assert.True(copy.ChecksumOk);
        Assert.True(copy.IsAtomBios);
        Assert.Equal(1024, copy.SizeBytes);
        Assert.Equal("113-APM6766SL-100", copy.PartNumber);
        Assert.Equal("2023/07/13 01:28", copy.BuildDate);
        Assert.Equal("RX-7800-XT-113-APM6766SL-100.rom", VideoBios.FileName("RX 7800 XT", copy));

        Assert.False(VideoBios.Describe(Rom(fixChecksum: false), "ACPI VFCT")!.ChecksumOk);
        Assert.Null(VideoBios.Describe(new byte[1024], "ACPI VFCT"));
    }

    [Fact]
    public void Report_row_and_the_json_leaves_the_image_out()
    {
        var gpu = new GpuInfo { Name = "AMD Radeon RX 7800 XT", VideoBios = VideoBios.Describe(Rom(), "ACPI VFCT") };
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(gpu);
        var rows = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("1 KB pela tabela ACPI VFCT · soma de verificação OK · 113-APM6766SL-100 · 2023/07/13 01:28", rows["Cópia da BIOS de vídeo"]);

        var json = System.Text.Json.JsonSerializer.Serialize(inventory, JsonReportWriter.Options);
        Assert.Contains("113-APM6766SL-100", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"image\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_parses_the_vbios_command()
    {
        var options = CliOptions.Parse(["vbios", "-o", @"C:\Temp"]);
        Assert.Equal(CliCommand.Vbios, options.Command);
        Assert.Equal(@"C:\Temp", options.OutputPath);
    }
}
