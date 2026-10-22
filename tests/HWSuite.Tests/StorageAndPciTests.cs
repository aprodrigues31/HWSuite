using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Pci;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
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
    public void Parses_nvme_identify_controller_strings()
    {
        var identify = new byte[4096];
        Encoding.ASCII.GetBytes("23140K800123        ").CopyTo(identify, 4);
        Encoding.ASCII.GetBytes("WD Green SN350 1TB".PadRight(40)).CopyTo(identify, 24);
        Encoding.ASCII.GetBytes("33006000").CopyTo(identify, 64);

        var (serial, model, firmware) = StorageDecoders.ParseNvmeIdentifyController(identify);

        Assert.Equal("23140K800123", serial);
        Assert.Equal("WD Green SN350 1TB", model);
        Assert.Equal("33006000", firmware);
    }

    [Theory]
    [InlineData("E823_8FA6_BF53_0001_001B_448B_4D44_4D9D.", "E823_8FA6_BF53_0001_001B_448B_4D44_4D9D")]
    [InlineData("0000000000000000", null)]
    [InlineData("0000_0000_0000_0000.", null)]
    [InlineData("  S7KHNJ0X123456A ", "S7KHNJ0X123456A")]
    public void Cleans_serial_numbers(string raw, string? expected) =>
        Assert.Equal(expected, StorageDecoders.CleanSerial(raw));

    [Fact]
    public void Recognizes_the_windows_nvme_identifier_form()
    {
        Assert.True(StorageDecoders.LooksLikeWindowsNvmeIdentifier("E823_8FA6_BF53_0001_001B_448B_4D44_4D9D"));
        Assert.False(StorageDecoders.LooksLikeWindowsNvmeIdentifier("23140K800123"));
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

public class PcieLinkTests
{
    private static readonly HWSuite.Core.Pci.PcieLink Internal = new(4, 16, 4, 16);

    [Fact]
    public void Radeon_behind_its_own_switch_reports_the_slot_link()
    {
        // RX 7800 XT: GPU (1002) -> downstream port (1002) -> upstream port (1002) -> CPU root port (1022).
        var slot = new HWSuite.Core.Pci.PcieLink(1, 16, 4, 16);
        HWSuite.Core.Pci.PcieNode[] chain =
        [
            new(HWSuite.Core.Pci.PciDeviceType.ExpressLegacyEndpoint, 0x1002, Internal),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressDownstreamSwitchPort, 0x1002, Internal),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressUpstreamSwitchPort, 0x1002, slot),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressRootPort, 0x1022, new(4, 16, 4, 16)),
        ];
        Assert.Equal(slot, HWSuite.Core.Pci.PcieTopology.SlotLink(chain));
    }

    [Fact]
    public void Card_in_a_chipset_slot_keeps_its_own_link()
    {
        // NVIDIA card (10DE) -> AMD chipset downstream port (1022) -> chipset upstream (1022) -> root port.
        var own = new HWSuite.Core.Pci.PcieLink(3, 4, 4, 16);
        HWSuite.Core.Pci.PcieNode[] chain =
        [
            new(HWSuite.Core.Pci.PciDeviceType.ExpressEndpoint, 0x10DE, own),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressDownstreamSwitchPort, 0x1022, new(3, 4, 3, 4)),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressUpstreamSwitchPort, 0x1022, new(3, 4, 4, 4)),
            new(HWSuite.Core.Pci.PciDeviceType.BridgeExpressRootPort, 0x1022, null),
        ];
        Assert.Equal(own, HWSuite.Core.Pci.PcieTopology.SlotLink(chain));
        Assert.True(own.RunsBelowMaxWidth);
    }

    [Fact]
    public void Single_device_and_empty_chain()
    {
        Assert.Equal(Internal, HWSuite.Core.Pci.PcieTopology.SlotLink([new(HWSuite.Core.Pci.PciDeviceType.ExpressEndpoint, 0x10DE, Internal)]));
        Assert.Null(HWSuite.Core.Pci.PcieTopology.SlotLink([]));
    }

    [Theory]
    [InlineData(1, 16, "PCIe 1.1 x16")]
    [InlineData(4, 16, "PCIe 4.0 x16")]
    [InlineData(5, 8, "PCIe 5.0 x8")]
    [InlineData(0, 16, null)]
    [InlineData(4, 0, null)]
    public void Link_description(int speed, int width, string? expected)
    {
        Assert.Equal(expected, HWSuite.Core.Pci.PcieLink.Describe(speed, width));
    }

    [Theory]
    [InlineData(4, 16, "PCIe 4.0 x16, rodando em PCIe 4.0 x16")]
    [InlineData(1, 16, "PCIe 4.0 x16, rodando em PCIe 1.1 x16 (economia de energia em repouso; sob carga chega ao máximo)")]
    [InlineData(4, 8, "PCIe 4.0 x16, rodando em PCIe 4.0 x8 (menos linhas do que a placa suporta: verifique o slot)")]
    public void Report_explains_a_slower_link(int speed, int width, string expected)
    {
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(new GpuInfo { Name = "AMD Radeon RX 7800 XT", PcieLink = new(speed, width, 4, 16) });
        var gpu = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups.Single();
        Assert.Equal(expected, gpu.Rows.Single(r => r.Label == "Interface").Value);
    }

    [Fact]
    public void Vendor_comes_from_the_instance_id()
    {
        // The reader takes each device's vendor from its PnP instance id.
        Assert.Equal((ushort)0x1002, PciIds.Parse(@"PCI\VEN_1002&DEV_747E&SUBSYS_24171849&REV_C8\6&2f8f9ce6&0&00000000")?.VendorId);
        Assert.Null(PciIds.Parse(@"ACPI\PNP0A08\0"));
    }
}
