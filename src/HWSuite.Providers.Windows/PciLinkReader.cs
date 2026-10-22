using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Pci;

namespace HWSuite.Providers.Windows;

/// <summary>
/// PCI Express link of a device, from the properties the PCI bus driver publishes for every device
/// node (DEVPKEY_PciDevice_*, pciprop.h). Walks up the device tree so a GPU behind its own on-card
/// switch reports the link to the motherboard slot. Works without administrator rights.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class PciLinkReader
{
    // {3AB22E31-8264-4B4E-9AF5-A8D2D8E33E62}: DEVPKEY_PciDevice_* property set.
    private static readonly Guid PciDeviceProperties = new(0x3AB22E31, 0x8264, 0x4B4E, 0x9A, 0xF5, 0xA8, 0xD2, 0xD8, 0xE3, 0x3E, 0x62);
    private const uint PidDeviceType = 1;
    private const uint PidCurrentLinkSpeed = 9;
    private const uint PidCurrentLinkWidth = 10;
    private const uint PidMaxLinkSpeed = 11;
    private const uint PidMaxLinkWidth = 12;

    private const uint CrSuccess = 0;
    private const uint CmGetIdListFilterEnumerator = 0x1;
    private const uint CmGetIdListFilterPresent = 0x100;
    private const uint DevPropTypeUInt32 = 0x7;
    private const int MaxDepth = 8;

    /// <summary>Slot link of the present PCI device with these ids, or null if Windows does not report it.</summary>
    public static PcieLink? ForDevice(ushort vendorId, ushort deviceId, uint subsystemId)
    {
        try
        {
            var instance = FindInstance(vendorId, deviceId, subsystemId);
            if (instance is null || CM_Locate_DevNodeW(out var node, instance, 0) != CrSuccess)
            {
                return null;
            }

            var chain = new List<PcieNode>();
            for (var depth = 0; depth < MaxDepth; depth++)
            {
                chain.Add(Node(node));
                if (CM_Get_Parent(out var parent, node, 0) != CrSuccess)
                {
                    break;
                }

                node = parent;
            }

            return PcieTopology.SlotLink(chain);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return null;
        }
    }

    private static PcieNode Node(uint devInst)
    {
        var type = ReadUInt32(devInst, PidDeviceType);
        var speed = ReadUInt32(devInst, PidCurrentLinkSpeed);
        var width = ReadUInt32(devInst, PidCurrentLinkWidth);
        var maxSpeed = ReadUInt32(devInst, PidMaxLinkSpeed);
        var maxWidth = ReadUInt32(devInst, PidMaxLinkWidth);
        var link = speed is null && maxSpeed is null
            ? null
            : new PcieLink((int)(speed ?? 0), (int)(width ?? 0), (int)(maxSpeed ?? 0), (int)(maxWidth ?? 0));
        return new PcieNode(type is { } t ? (PciDeviceType)t : null, VendorOf(InstanceId(devInst)), link);
    }

    private static uint? ReadUInt32(uint devInst, uint pid)
    {
        var key = new DevPropKey(PciDeviceProperties, pid);
        uint value = 0;
        uint size = sizeof(uint);
        return CM_Get_DevNode_PropertyW(devInst, &key, out var type, (byte*)&value, ref size, 0) == CrSuccess
            && type == DevPropTypeUInt32 && size == sizeof(uint)
            ? value
            : null;
    }

    private static string? InstanceId(uint devInst)
    {
        var buffer = stackalloc char[400];
        return CM_Get_Device_IDW(devInst, buffer, 400, 0) == CrSuccess ? new string(buffer) : null;
    }

    /// <summary>"PCI\VEN_1002&amp;DEV_747E&amp;SUBSYS_...\..." -> 0x1002.</summary>
    internal static ushort? VendorOf(string? instanceId)
    {
        var id = instanceId is null ? null : PciIds.Parse(instanceId);
        return id?.VendorId;
    }

    private static string? FindInstance(ushort vendorId, ushort deviceId, uint subsystemId)
    {
        const string Filter = "PCI";
        const uint Flags = CmGetIdListFilterEnumerator | CmGetIdListFilterPresent;
        if (CM_Get_Device_ID_List_SizeW(out var length, Filter, Flags) != CrSuccess || length == 0)
        {
            return null;
        }

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (CM_Get_Device_ID_ListW(Filter, p, length, Flags) != CrSuccess)
            {
                return null;
            }
        }

        string? fallback = null;
        foreach (var instance in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (PciIds.Parse(instance) is not { } id || id.VendorId != vendorId || id.DeviceId != deviceId)
            {
                continue;
            }

            if (subsystemId == 0 || id.SubsystemId == subsystemId)
            {
                return instance;
            }

            fallback ??= instance;
        }

        return fallback;
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
    private static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Device_IDW(uint devInst, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_PropertyW(uint devInst, DevPropKey* key, out uint propertyType, byte* buffer, ref uint bufferSize, uint flags);
}
