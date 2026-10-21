using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Model;

namespace HWSuite.Core.Storage;

/// <summary>Identity fields from a Windows STORAGE_DEVICE_DESCRIPTOR.</summary>
public sealed record StorageDeviceDescriptor(
    string? Vendor,
    string? Product,
    string? Revision,
    string? SerialNumber,
    int BusTypeCode,
    bool Removable);

/// <summary>
/// Pure decoders for the buffers returned by storage IOCTLs. Kept free of P/Invoke so they
/// can be tested with captured buffers on any OS.
/// </summary>
public static class StorageDecoders
{
    /// <summary>
    /// Layout (winioctl.h): Version(4) Size(4) DeviceType(1) DeviceTypeModifier(1) RemovableMedia(1)
    /// CommandQueueing(1) VendorIdOffset(4) ProductIdOffset(4) ProductRevisionOffset(4)
    /// SerialNumberOffset(4) BusType(4) RawPropertiesLength(4) RawDeviceProperties[...].
    /// Offsets of zero mean "not provided"; strings are NUL-terminated ASCII.
    /// </summary>
    public static StorageDeviceDescriptor ParseDeviceDescriptor(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 36)
        {
            throw new FormatException("STORAGE_DEVICE_DESCRIPTOR is shorter than its fixed part.");
        }

        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]), (uint)buffer.Length);
        var data = buffer[..Math.Max(size, 36)];

        return new StorageDeviceDescriptor(
            Vendor: StringAt(data, 12),
            Product: StringAt(data, 16),
            Revision: StringAt(data, 20),
            SerialNumber: StringAt(data, 24),
            BusTypeCode: (int)BinaryPrimitives.ReadUInt32LittleEndian(data[28..]),
            Removable: data[10] != 0);
    }

    /// <summary>DISK_GEOMETRY_EX: DISK_GEOMETRY (24 bytes) followed by DiskSize (LARGE_INTEGER).</summary>
    public static long? ParseDiskSize(ReadOnlySpan<byte> geometryEx) =>
        geometryEx.Length >= 32 ? BinaryPrimitives.ReadInt64LittleEndian(geometryEx[24..]) : null;

    /// <summary>
    /// NVMe SMART / Health Information log page 02h (512 bytes), per the NVMe Base Specification.
    /// 128-bit counters are read through their low 64 bits, which is ample for real drives.
    /// </summary>
    public static DiskHealth ParseNvmeHealthLog(ReadOnlySpan<byte> log)
    {
        if (log.Length < 176)
        {
            throw new FormatException("NVMe health log page is shorter than 176 bytes.");
        }

        // One "data unit" is 1000 units of 512 bytes.
        const long DataUnitBytes = 512_000;
        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log[1..]);

        return new DiskHealth
        {
            CriticalWarning = log[0],
            TemperatureCelsius = kelvin == 0 ? null : kelvin - 273,
            AvailableSparePercent = log[3],
            PercentageUsed = log[5],
            DataReadBytes = SaturatingMultiply(Counter(log, 32), DataUnitBytes),
            DataWrittenBytes = SaturatingMultiply(Counter(log, 48), DataUnitBytes),
            PowerCycles = Counter(log, 112),
            PowerOnHours = Counter(log, 128),
            UnsafeShutdowns = Counter(log, 144),
            MediaErrors = Counter(log, 160),
        };
    }

    public static string BusTypeName(int code) => code switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown",
    };

    private static string? StringAt(ReadOnlySpan<byte> data, int fieldOffset)
    {
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[fieldOffset..]);
        if (offset <= 0 || offset >= data.Length)
        {
            return null;
        }

        var tail = data[offset..];
        var end = tail.IndexOf((byte)0);
        var text = Encoding.ASCII.GetString(end < 0 ? tail : tail[..end]).Trim();
        return text.Length == 0 ? null : text;
    }

    private static long Counter(ReadOnlySpan<byte> log, int offset) =>
        (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(log[offset..]), long.MaxValue);

    private static long SaturatingMultiply(long value, long factor) =>
        value > long.MaxValue / factor ? long.MaxValue : value * factor;
}
