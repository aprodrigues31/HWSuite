using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Model;
using HWSuite.Core.Smbios;

namespace HWSuite.Tests;

/// <summary>Builds SMBIOS tables byte by byte and checks the decoder against DSP0134 offsets.</summary>
public class SmbiosTests
{
    private sealed class TableBuilder
    {
        private readonly List<byte> _bytes = [];
        private ushort _handle;

        /// <summary>Adds a structure; <paramref name="body"/> starts at offset 04h.</summary>
        public TableBuilder Add(byte type, byte[] body, params string[] strings)
        {
            var length = (byte)(4 + body.Length);
            _bytes.Add(type);
            _bytes.Add(length);
            _bytes.Add((byte)(_handle & 0xFF));
            _bytes.Add((byte)(_handle >> 8));
            _handle++;
            _bytes.AddRange(body);
            if (strings.Length == 0)
            {
                _bytes.AddRange([0, 0]);
            }
            else
            {
                foreach (var s in strings)
                {
                    _bytes.AddRange(Encoding.ASCII.GetBytes(s));
                    _bytes.Add(0);
                }

                _bytes.Add(0);
            }

            return this;
        }

        /// <summary>Wraps the table in the 8-byte RawSMBIOSData header Windows returns.</summary>
        public byte[] WindowsRaw(byte major, byte minor)
        {
            var header = new byte[8];
            header[1] = major;
            header[2] = minor;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)_bytes.Count);
            return [.. header, .. _bytes];
        }
    }

    /// <summary>Formatted-area bytes from offset 04h, with helpers to poke fields at absolute offsets.</summary>
    private sealed class Body(int length)
    {
        private readonly byte[] _data = new byte[length - 4];

        public Body B(int offset, byte value)
        {
            _data[offset - 4] = value;
            return this;
        }

        public Body W(int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(offset - 4), value);
            return this;
        }

        public Body D(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(offset - 4), value);
            return this;
        }

        public Body Raw(int offset, byte[] value)
        {
            value.CopyTo(_data, offset - 4);
            return this;
        }

        public byte[] Bytes => _data;
    }

    private static readonly byte[] UuidBytes =
        [0x78, 0x56, 0x34, 0x12, 0x34, 0x12, 0x78, 0x56, 0x9A, 0xBC, 0xDE, 0xF0, 0x12, 0x34, 0x56, 0x78];

    private static HardwareInventory DecodeSample()
    {
        var table = new TableBuilder()
            .Add(0, new Body(0x18).B(0x04, 1).B(0x05, 2).B(0x08, 3).Bytes, "American Megatrends Inc.", "1.40", "05/12/2025")
            .Add(1, new Body(0x1B).B(0x04, 1).B(0x05, 2).B(0x07, 3).Raw(0x08, UuidBytes).B(0x19, 4).B(0x1A, 5).Bytes,
                "Positivo", "Master D3400", "SN-SYS-001", "None", "Master")
            .Add(2, new Body(0x0F).B(0x04, 1).B(0x05, 2).B(0x06, 3).B(0x07, 4).Bytes, "ASUSTeK COMPUTER INC.", "PRIME B760M-A", "Rev 1.xx", "BOARD-SN")
            .Add(3, new Body(0x0D).B(0x04, 1).B(0x05, 3).Bytes, "Default string")
            .Add(4, new Body(0x30).B(0x04, 1).B(0x05, 3).B(0x07, 2).B(0x10, 3).W(0x12, 100).W(0x14, 4900).W(0x16, 2500)
                .B(0x18, 0x41).B(0x23, 10).B(0x25, 16).Bytes, "LGA1700", "Intel(R) Corporation", "13th Gen Intel(R) Core(TM) i5-13400")
            .Add(16, new Body(0x17).B(0x05, 3).D(0x07, 0x8000_0000).W(0x0D, 4).Raw(0x0F, BitConverter.GetBytes(192L * 1024 * 1024 * 1024)).Bytes)
            .Add(17, new Body(0x5C).W(0x0C, 16384).B(0x0E, 0x09).B(0x10, 1).B(0x11, 2).B(0x12, 0x22).W(0x15, 5600)
                .B(0x17, 3).B(0x18, 4).B(0x1A, 5).B(0x1B, 0x02).W(0x20, 4800).Bytes,
                "DIMM_A1", "BANK 0", "Kingston", "1234ABCD", "KF556C40-16")
            .Add(17, new Body(0x5C).W(0x0C, 0).B(0x10, 1).Bytes, "DIMM_A2")
            .Add(17, new Body(0x5C).W(0x0C, 0x7FFF).D(0x1C, 65536).B(0x0E, 0x0D).B(0x10, 1).B(0x12, 0x23).B(0x17, 2).Bytes,
                "DIMM_B1", "Unknown")
            .Add(127, [])
            .WindowsRaw(3, 4);

        var inventory = new HardwareInventory();
        SmbiosDecoder.Apply(SmbiosTable.ParseWindowsRaw(table), inventory);
        return inventory;
    }

    [Fact]
    public void Parses_all_structures_including_the_end_marker()
    {
        var raw = new TableBuilder().Add(0, new Body(0x18).B(0x04, 1).Bytes, "Vendor").Add(2, new Body(0x08).Bytes).Add(127, []).WindowsRaw(2, 8);

        var table = SmbiosTable.ParseWindowsRaw(raw);

        Assert.Equal("2.8", table.Version);
        Assert.Equal([0, 2, 127], table.Structures.Select(s => (int)s.Type));
        Assert.Equal("Vendor", table.Structures[0].Text(0x04));
        Assert.Null(table.Structures[1].Text(0x04));
    }

    [Fact]
    public void Decodes_bios_system_and_baseboard()
    {
        var inv = DecodeSample();

        Assert.Equal("American Megatrends Inc.", inv.Bios!.Vendor);
        Assert.Equal("1.40", inv.Bios.Version);
        Assert.Equal("05/12/2025", inv.Bios.ReleaseDate);
        Assert.Equal("3.4", inv.Bios.SmbiosVersion);

        Assert.Equal("Positivo", inv.System!.Manufacturer);
        Assert.Equal("Master D3400", inv.System.Product);
        Assert.Equal("SN-SYS-001", inv.System.SerialNumber);
        Assert.Equal("Master", inv.System.Family);
        Assert.Null(inv.System.Sku); // "None" is an OEM placeholder
        Assert.Equal("Desktop", inv.System.ChassisType);
        Assert.Equal("12345678-1234-5678-9ABC-DEF012345678", inv.System.Uuid);

        Assert.Equal("ASUSTeK COMPUTER INC.", inv.Motherboard!.Manufacturer);
        Assert.Equal("PRIME B760M-A", inv.Motherboard.Product);
        Assert.Equal("BOARD-SN", inv.Motherboard.SerialNumber);
    }

    [Fact]
    public void Decodes_populated_processor_socket()
    {
        var cpu = Assert.Single(DecodeSample().Processors);

        Assert.Equal("LGA1700", cpu.Socket);
        Assert.Equal("13th Gen Intel(R) Core(TM) i5-13400", cpu.Name);
        Assert.Equal((10, 16), (cpu.Cores, cpu.Threads));
        Assert.Equal((100, 4900, 2500), (cpu.BusClockMhz, cpu.MaxClockMhz, cpu.CurrentClockMhz));
    }

    [Fact]
    public void Decodes_memory_array_and_modules_skipping_empty_slots()
    {
        var mem = DecodeSample().Memory!;

        Assert.Equal(4, mem.SlotCount);
        Assert.Equal(192L * 1024 * 1024 * 1024, mem.MaxCapacityBytes);
        Assert.Equal(2, mem.Modules.Count);

        var a1 = mem.Modules[0];
        Assert.Equal("DIMM_A1", a1.Slot);
        Assert.Equal(16L * 1024 * 1024 * 1024, a1.SizeBytes);
        Assert.Equal(("DDR5", "DIMM"), (a1.Type, a1.FormFactor));
        Assert.Equal((5600, 4800, 2), (a1.SpeedMts, a1.ConfiguredSpeedMts, a1.Ranks));
        Assert.Equal(("Kingston", "KF556C40-16"), (a1.Manufacturer, a1.PartNumber));

        var b1 = mem.Modules[1];
        Assert.Equal(64L * 1024 * 1024 * 1024, b1.SizeBytes); // Extended Size path (0x7FFF)
        Assert.Equal(("LPDDR5", "SODIMM"), (b1.Type, b1.FormFactor));
        Assert.Null(b1.Manufacturer); // "Unknown" placeholder removed
    }

    [Theory]
    [InlineData(0x0400, 1024L * 1024 * 1024)] // MiB units
    [InlineData(0x8200, 512L * 1024)] // bit 15 set: KiB units
    [InlineData(0xFFFF, 0L)] // unknown size
    public void Memory_device_size_units(int sizeWord, long expected)
    {
        var raw = new TableBuilder().Add(17, new Body(0x22).W(0x0C, (ushort)sizeWord).Bytes).WindowsRaw(3, 0);
        var device = SmbiosTable.ParseWindowsRaw(raw).Structures[0];

        Assert.Equal(expected, SmbiosDecoder.MemoryDeviceSizeBytes(device));
    }

    [Fact]
    public void Uuid_before_2_6_is_big_endian_and_blank_uuids_are_dropped()
    {
        Assert.Equal("78563412-3412-7856-9ABC-DEF012345678", SmbiosDecoder.FormatUuid(UuidBytes, 2, 5));
        Assert.Null(SmbiosDecoder.FormatUuid(new byte[16], 3, 0));
        Assert.Null(SmbiosDecoder.FormatUuid(Enumerable.Repeat((byte)0xFF, 16).ToArray(), 3, 0));
    }

    [Fact]
    public void Truncated_table_does_not_throw()
    {
        var raw = new TableBuilder().Add(0, new Body(0x18).B(0x04, 1).Bytes, "Vendor").WindowsRaw(3, 0);
        var truncated = raw[..^5];

        var table = SmbiosTable.ParseWindowsRaw(truncated);

        Assert.Single(table.Structures);
    }
}
