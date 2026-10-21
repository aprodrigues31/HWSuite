using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Pci;
using HWSuite.Core.Storage;

namespace HWSuite.Tests;

public class StorageAndPciTests
{
    private static byte[] DeviceDescriptor(string? vendor, string product, string revision, string serial, int busType, bool removable)
    {
        var buffer = new byte[256];
        var cursor = 40;
        int Put(string? text)
        {
            if (text is null)
            {
                return 0;
            }

            var at = cursor;
            Encoding.ASCII.GetBytes(text).CopyTo(buffer, at);
            cursor += text.Length + 1;
            return at;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 40);
        buffer[10] = removable ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), (uint)Put(vendor));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), (uint)Put(product));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), (uint)Put(revision));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), (uint)Put(serial));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), (uint)busType);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)cursor);
        return buffer;
    }

    [Fact]
    public void Parses_storage_device_descriptor()
    {
        var buffer = DeviceDescriptor(null, "Samsung SSD 990 PRO 2TB ", "4B2QJXD7", "S7KHNJ0X123456A", busType: 17, removable: false);

        var d = StorageDecoders.ParseDeviceDescriptor(buffer);

        Assert.Null(d.Vendor);
        Assert.Equal("Samsung SSD 990 PRO 2TB", d.Product);
        Assert.Equal("4B2QJXD7", d.Revision);
        Assert.Equal("S7KHNJ0X123456A", d.SerialNumber);
        Assert.Equal("NVMe", StorageDecoders.BusTypeName(d.BusTypeCode));
        Assert.False(d.Removable);
    }

    [Fact]
    public void Parses_disk_size_from_geometry_ex()
    {
        var geometry = new byte[32];
        BinaryPrimitives.WriteInt64LittleEndian(geometry.AsSpan(24), 2_000_398_934_016);

        Assert.Equal(2_000_398_934_016, StorageDecoders.ParseDiskSize(geometry));
        Assert.Null(StorageDecoders.ParseDiskSize(new byte[16]));
    }

    [Fact]
    public void Parses_nvme_health_log()
    {
        var log = new byte[512];
        log[0] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), 273 + 41);
        log[3] = 100;
        log[5] = 3;
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(32), 20_000_000);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48), 30_000_000);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(112), 812);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(128), 4321);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(144), 17);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(160), 0);

        var h = StorageDecoders.ParseNvmeHealthLog(log);

        Assert.Equal(41, h.TemperatureCelsius);
        Assert.Equal((100, 3), (h.AvailableSparePercent, h.PercentageUsed));
        Assert.Equal(20_000_000L * 512_000, h.DataReadBytes);
        Assert.Equal(30_000_000L * 512_000, h.DataWrittenBytes);
        Assert.Equal((812L, 4321L, 17L, 0L), (h.PowerCycles, h.PowerOnHours, h.UnsafeShutdowns, h.MediaErrors));
    }

    [Fact]
    public void Huge_nvme_counters_saturate_instead_of_overflowing()
    {
        var log = new byte[512];
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48), ulong.MaxValue);

        Assert.Equal(long.MaxValue, StorageDecoders.ParseNvmeHealthLog(log).DataWrittenBytes);
    }

    [Fact]
    public void Parses_pci_hardware_ids()
    {
        var id = PciIds.Parse(@"PCI\VEN_10DE&DEV_2684&SUBSYS_16F110DE&REV_A1");

        Assert.NotNull(id);
        Assert.Equal((ushort)0x10DE, id.VendorId);
        Assert.Equal((ushort)0x2684, id.DeviceId);
        Assert.Equal(0x16F110DEu, id.SubsystemId);
        Assert.Equal((byte)0xA1, id.Revision);
        Assert.Equal("NVIDIA", PciIds.VendorName(id.VendorId));

        var lower = PciIds.Parse("pci\\ven_8086&dev_a7a0");
        Assert.Equal((ushort)0xA7A0, lower!.DeviceId);
        Assert.Null(lower.SubsystemId);

        Assert.Null(PciIds.Parse("USB\\VID_046D&PID_C52B"));
    }
}
