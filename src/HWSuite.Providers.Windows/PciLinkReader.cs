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
    private const uint AllocLogConf = 0x2; // ALLOC_LOG_CONF: resources currently assigned
    private const uint ResTypeMem = 0x1;
    private const uint ResTypeMemLarge = 0x7;

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

    /// <summary>
    /// Largest memory range (BAR) Windows assigned to the device. Without Resizable BAR a GPU exposes a
    /// 256 MB window into its VRAM; with it, the window covers the whole VRAM (e.g. 16 GB).
    /// </summary>
    public static long? LargestMemoryWindow(ushort vendorId, ushort deviceId, uint subsystemId)
    {
        try
        {
            var instance = FindInstance(vendorId, deviceId, subsystemId);
            if (instance is null || CM_Locate_DevNodeW(out var node, instance, 0) != CrSuccess)
            {
                return null;
            }

            if (CM_Get_First_Log_Conf(out var logConf, node, AllocLogConf) != CrSuccess)
            {
                return null;
            }

            long largest = 0;
            try
            {
                foreach (var resourceType in new[] { ResTypeMem, ResTypeMemLarge })
                {
                    var current = logConf;
                    while (CM_Get_Next_Res_Des(out var next, current, resourceType, out _, 0) == CrSuccess)
                    {
                        if (current != logConf)
                        {
                            _ = CM_Free_Res_Des_Handle(current);
                        }

                        current = next;
                        largest = Math.Max(largest, RangeLength(next));
                    }

                    if (current != logConf)
                    {
                        _ = CM_Free_Res_Des_Handle(current);
                    }
                }
            }
            finally
            {
                _ = CM_Free_Log_Conf_Handle(logConf);
            }

            return largest > 0 ? largest : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>MEM_DES / MEM_LARGE_DES: Count(4) Type(4) Alloc_Base(8) Alloc_End(8) Flags(4) Reserved(4).</summary>
    private static long RangeLength(nint resDes)
    {
        if (CM_Get_Res_Des_Data_Size(out var size, resDes, 0) != CrSuccess || size < 24)
        {
            return 0;
        }

        var buffer = stackalloc byte[(int)Math.Min(size, 4096u)];
        if (CM_Get_Res_Des_Data(resDes, buffer, Math.Min(size, 4096u), 0) != CrSuccess)
        {
            return 0;
        }

        var start = *(ulong*)(buffer + 8);
        var end = *(ulong*)(buffer + 16);
        return end >= start ? (long)(end - start + 1) : 0;
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
    private static partial uint CM_Get_First_Log_Conf(out nint logConf, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Next_Res_Des(out nint next, nint current, uint forResource, out uint resourceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Res_Des_Data_Size(out uint size, nint resDes, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Res_Des_Data(nint resDes, byte* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Free_Res_Des_Handle(nint resDes);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Free_Log_Conf_Handle(nint logConf);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_PropertyW(uint devInst, DevPropKey* key, out uint propertyType, byte* buffer, ref uint bufferSize, uint flags);
}
