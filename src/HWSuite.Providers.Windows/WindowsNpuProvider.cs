using System.Runtime.InteropServices;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Pci;

namespace HWSuite.Providers.Windows;

/// <summary>
/// NPUs: the present devices of the Windows "Neural processors" class (ComputeAccelerator,
/// {f01a9d53-3ff6-48d2-9f97-c8a7004be10c}), where AMD Ryzen AI, Intel AI Boost and Qualcomm Hexagon
/// drivers register. Name, maker and driver come from the device properties; no administrator rights needed.
/// </summary>
public sealed unsafe partial class WindowsNpuProvider : IInventoryProvider
{
    private const string ComputeAcceleratorClass = "{f01a9d53-3ff6-48d2-9f97-c8a7004be10c}";
    private const uint CrSuccess = 0;
    private const uint FilterClass = 0x200; // CM_GETIDLIST_FILTER_CLASS
    private const uint FilterPresent = 0x100; // CM_GETIDLIST_FILTER_PRESENT
    private const uint TypeString = 0x12; // DEVPROP_TYPE_STRING
    private const uint TypeFileTime = 0x10; // DEVPROP_TYPE_FILETIME

    private static readonly Guid DeviceProperties = new("a45c254e-df1c-4efd-8020-67d146a850e0"); // DEVPKEY_Device_*
    private static readonly Guid DriverProperties = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6"); // DEVPKEY_Device_Driver*

    public string Name => "NPU";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var instance in Instances())
        {
            if (CM_Locate_DevNodeW(out var node, instance, 0) != CrSuccess)
            {
                continue;
            }

            var id = PciIds.Parse(instance);
            var npu = new NpuInfo
            {
                Name = String(node, DeviceProperties, 14) ?? String(node, DeviceProperties, 2), // FriendlyName, DeviceDesc
                Vendor = id is { } pci ? PciIds.VendorName(pci.VendorId) ?? String(node, DeviceProperties, 13) : String(node, DeviceProperties, 13), // Manufacturer
                PciVendorId = id is { } v ? PciIds.FormatId(v.VendorId) : null,
                PciDeviceId = id is { } d ? PciIds.FormatId(d.DeviceId) : null,
                DriverVersion = String(node, DriverProperties, 3),
                DriverDate = Date(node),
            };
            inventory.Npus.Add(npu);
        }
    }

    private static List<string> Instances()
    {
        const uint Flags = FilterClass | FilterPresent;
        if (CM_Get_Device_ID_List_SizeW(out var length, ComputeAcceleratorClass, Flags) != CrSuccess || length <= 1)
        {
            return [];
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (CM_Get_Device_ID_ListW(ComputeAcceleratorClass, p, length, Flags) != CrSuccess)
            {
                return [];
            }
        }

        return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static string? String(uint node, Guid set, uint pid)
    {
        var key = new DevPropKey(set, pid);
        uint size = 0;
        _ = CM_Get_DevNode_PropertyW(node, &key, out _, null, ref size, 0);
        if (size < 2 || size > 4096)
        {
            return null;
        }

        var buffer = stackalloc byte[(int)size];
        return CM_Get_DevNode_PropertyW(node, &key, out var type, buffer, ref size, 0) == CrSuccess && type == TypeString
            ? new string((char*)buffer).Trim()
            : null;
    }

    /// <summary>DEVPKEY_Device_DriverDate (FILETIME) as "yyyy-MM-dd".</summary>
    private static string? Date(uint node)
    {
        var key = new DevPropKey(DriverProperties, 2);
        long fileTime = 0;
        uint size = sizeof(long);
        return CM_Get_DevNode_PropertyW(node, &key, out var type, (byte*)&fileTime, ref size, 0) == CrSuccess && type == TypeFileTime && fileTime > 0
            ? DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid fmtid, uint pid)
    {
        public readonly Guid FmtId = fmtid;
        public readonly uint Pid = pid;
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_ListW(string filter, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_PropertyW(uint devInst, DevPropKey* key, out uint propertyType, byte* buffer, ref uint bufferSize, uint flags);
}
