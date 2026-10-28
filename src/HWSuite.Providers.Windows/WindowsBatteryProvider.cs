using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Power;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Batteries and USB UPSes through the battery class driver: each GUID_DEVINTERFACE_BATTERY device is
/// opened and asked for its tag, information, status, names, dates and estimates (IOCTL_BATTERY_*). Works
/// without administrator rights; a desktop without a UPS simply has none.
/// </summary>
public sealed unsafe partial class WindowsBatteryProvider : IInventoryProvider
{
    private static readonly Guid BatteryInterface = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private const uint IoctlQueryTag = 0x294040;
    private const uint IoctlQueryInformation = 0x294044;
    private const uint IoctlQueryStatus = 0x29404C;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;

    private enum Level
    {
        Information = 0,
        Temperature = 2,
        EstimatedTime = 3,
        DeviceName = 4,
        ManufactureDate = 5,
        ManufactureName = 6,
        SerialNumber = 8,
    }

    public string Name => "Battery";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var path in Interfaces())
        {
            if (Read(path) is { } battery)
            {
                inventory.Batteries.Add(battery);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static BatteryInfo? Read(string path)
    {
        using var device = Native.CreateFile(path, GenericRead | GenericWrite, Native.FileShareReadWrite, 0, Native.OpenExisting, 0, 0);
        if (device.IsInvalid)
        {
            return null;
        }

        Span<byte> wait = stackalloc byte[4]; // timeout 0: answer now
        if (Native.Ioctl(device, IoctlQueryTag, wait, 4) is not { Length: 4 } tagBytes)
        {
            return null;
        }

        var tag = BinaryPrimitives.ReadUInt32LittleEndian(tagBytes);
        if (tag == 0)
        {
            return null; // no battery in this slot
        }

        byte[]? Query(Level level, int size)
        {
            Span<byte> input = stackalloc byte[12]; // BATTERY_QUERY_INFORMATION { tag, level, atRate }
            BinaryPrimitives.WriteUInt32LittleEndian(input, tag);
            BinaryPrimitives.WriteInt32LittleEndian(input[4..], (int)level);
            return Native.Ioctl(device, IoctlQueryInformation, input, size);
        }

        string? Text(Level level) => Query(level, 512) is { Length: >= 2 } bytes
            ? new string(MemoryMarshal.Cast<byte, char>(bytes)).Split('\0')[0].Trim() is { Length: > 0 } s ? s : null
            : null;

        var battery = new BatteryInfo
        {
            Name = Text(Level.DeviceName),
            Manufacturer = Text(Level.ManufactureName),
            SerialNumber = Text(Level.SerialNumber),
        };

        if (Query(Level.Information, 36) is { } info)
        {
            Batteries.ApplyInformation(battery, info);
        }

        if (Query(Level.ManufactureDate, 4) is { } date)
        {
            battery.ManufactureDate = Batteries.ManufactureDate(date);
        }

        if (Query(Level.Temperature, 4) is { Length: 4 } temperature)
        {
            battery.TemperatureCelsius = Batteries.Celsius(BinaryPrimitives.ReadUInt32LittleEndian(temperature));
        }

        if (Query(Level.EstimatedTime, 4) is { Length: 4 } estimate)
        {
            battery.EstimatedTime = Batteries.EstimatedTime(BinaryPrimitives.ReadUInt32LittleEndian(estimate));
        }

        Span<byte> waitStatus = stackalloc byte[20]; // BATTERY_WAIT_STATUS { tag, timeout 0, ... }
        BinaryPrimitives.WriteUInt32LittleEndian(waitStatus, tag);
        if (Native.Ioctl(device, IoctlQueryStatus, waitStatus, 16) is { } status)
        {
            Batteries.ApplyStatus(battery, status);
        }

        return battery;
    }

    private static List<string> Interfaces()
    {
        var guid = BatteryInterface;
        if (CM_Get_Device_Interface_List_SizeW(out var length, &guid, null, 0) != 0 || length <= 1)
        {
            return [];
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (CM_Get_Device_Interface_ListW(&guid, null, p, length, 0) != 0)
            {
                return [];
            }
        }

        return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, Guid* interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_ListW(Guid* interfaceClass, string? deviceId, char* buffer, uint length, uint flags);
}
