using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Storage;
using Microsoft.Win32.SafeHandles;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Physical disks through storage IOCTLs opened with no access rights (no elevation needed):
/// identity, bus, size, SSD/HDD and, for NVMe, the SMART / Health log page.
/// SATA SMART attributes need ATA pass-through with admin rights and come in a later phase.
/// </summary>
public sealed class WindowsStorageProvider : IInventoryProvider
{
    private const int MaxPhysicalDrives = 64;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolTypeNvme = 3;
    private const int NvmeDataTypeLogPage = 2;
    private const int NvmeLogPageHealthInfo = 2;
    private const int ProtocolSpecificDataSize = 40;
    private const int NvmeLogSize = 512;
    private const int BusTypeNvme = 17;

    public string Name => "Storage";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(context);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        for (var index = 0; index < MaxPhysicalDrives; index++)
        {
            using var handle = Native.CreateFile(
                string.Create(CultureInfo.InvariantCulture, $@"\\.\PhysicalDrive{index}"),
                0, Native.FileShareReadWrite, 0, Native.OpenExisting, 0, 0);

            if (handle.IsInvalid)
            {
                continue; // numbering can have gaps
            }

            var disk = ReadDisk(handle, index, context);
            if (disk is not null)
            {
                inventory.Disks.Add(disk);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static DiskInfo? ReadDisk(SafeFileHandle handle, int index, ProviderContext context)
    {
        var descriptorBuffer = Native.Ioctl(handle, Native.IoctlStorageQueryProperty, PropertyQuery(StorageDeviceProperty), 1024);
        if (descriptorBuffer is null)
        {
            context.Warn(string.Create(CultureInfo.InvariantCulture, $"PhysicalDrive{index}: device descriptor unavailable (error {Marshal.GetLastPInvokeError()})"));
            return null;
        }

        var descriptor = StorageDecoders.ParseDeviceDescriptor(descriptorBuffer);
        var disk = new DiskInfo
        {
            Index = index,
            Vendor = descriptor.Vendor,
            Model = descriptor.Product,
            Firmware = descriptor.Revision,
            SerialNumber = descriptor.SerialNumber,
            BusType = StorageDecoders.BusTypeName(descriptor.BusTypeCode),
            Removable = descriptor.Removable,
        };

        var geometry = Native.Ioctl(handle, Native.IoctlDiskGetDriveGeometryEx, [], 256);
        if (geometry is not null)
        {
            disk.SizeBytes = StorageDecoders.ParseDiskSize(geometry);
        }

        var seek = Native.Ioctl(handle, Native.IoctlStorageQueryProperty, PropertyQuery(StorageDeviceSeekPenaltyProperty), 12);
        if (seek is { Length: >= 9 })
        {
            disk.MediaType = seek[8] != 0 ? "HDD" : "SSD";
        }

        if (descriptor.BusTypeCode == BusTypeNvme)
        {
            disk.MediaType ??= "SSD";
            disk.Health = ReadNvmeHealth(handle);
            if (disk.Health is null)
            {
                context.Info(string.Create(CultureInfo.InvariantCulture, $"PhysicalDrive{index}: NVMe health log not available through the inbox driver"));
            }
        }

        return disk;
    }

    /// <summary>STORAGE_PROPERTY_QUERY: PropertyId(4) QueryType(4)=PropertyStandardQuery AdditionalParameters[1] (+padding).</summary>
    private static byte[] PropertyQuery(int propertyId)
    {
        var query = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(query, propertyId);
        return query;
    }

    [SupportedOSPlatform("windows")]
    private static DiskHealth? ReadNvmeHealth(SafeFileHandle handle)
    {
        // STORAGE_PROPERTY_QUERY header (8) + STORAGE_PROTOCOL_SPECIFIC_DATA (40) + log page (512).
        var buffer = new byte[8 + ProtocolSpecificDataSize + NvmeLogSize];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, StorageDeviceProtocolSpecificProperty);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 0); // PropertyStandardQuery
        var protocol = span[8..];
        BinaryPrimitives.WriteInt32LittleEndian(protocol, ProtocolTypeNvme);
        BinaryPrimitives.WriteInt32LittleEndian(protocol[4..], NvmeDataTypeLogPage);
        BinaryPrimitives.WriteInt32LittleEndian(protocol[8..], NvmeLogPageHealthInfo);
        BinaryPrimitives.WriteInt32LittleEndian(protocol[16..], ProtocolSpecificDataSize); // ProtocolDataOffset
        BinaryPrimitives.WriteInt32LittleEndian(protocol[20..], NvmeLogSize); // ProtocolDataLength

        var output = Native.Ioctl(handle, Native.IoctlStorageQueryProperty, buffer, buffer.Length);
        if (output is null || output.Length < 8 + ProtocolSpecificDataSize)
        {
            return null;
        }

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR: Version(4) Size(4) ProtocolSpecificData(40); offset is relative to that struct.
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8 + 16));
        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8 + 20));
        var start = 8 + dataOffset;
        if (dataLength < 176 || start + dataLength > output.Length)
        {
            return null;
        }

        return StorageDecoders.ParseNvmeHealthLog(output.AsSpan(start, dataLength));
    }
}
