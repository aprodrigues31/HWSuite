using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Pci;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Graphics adapters as DXGI sees them (only devices actually present), enriched with the
/// driver date and video BIOS string from the display class registry key.
/// Detailed GPU sensors and chip databases arrive in later phases (NVML / ADLX / IGCL).
/// </summary>
public sealed class DxgiGpuProvider : IInventoryProvider
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public string Name => "DXGI";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registry = ReadDisplayClass();
        foreach (var gpu in EnumerateAdapters())
        {
            if (registry.FirstOrDefault(r => r.Matches(gpu)) is { } entry)
            {
                gpu.DriverDate = DateText.Normalize(entry.DriverDate);
                if (entry.MemorySizeBytes is > 0 and var exact && exact >= (gpu.DedicatedMemoryBytes ?? 0))
                {
                    gpu.DedicatedMemoryBytes = exact; // DXGI subtracts reserved memory; the driver reports the board size
                }

                gpu.BiosVersion = entry.BiosString;
                gpu.DriverVersion ??= entry.DriverVersion;
            }

            inventory.Gpus.Add(gpu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe List<GpuInfo> EnumerateAdapters()
    {
        var result = new List<GpuInfo>();
        var hr = Native.CreateDXGIFactory1(IidFactory1, out var factory);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            // IDXGIFactory1 vtable: IUnknown(0-2), IDXGIObject(3-6), IDXGIFactory(7-11), EnumAdapters1 = 12.
            var factoryVtbl = *(nint**)factory;
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)factoryVtbl[12];

            for (uint i = 0; ; i++)
            {
                nint adapter;
                hr = enumAdapters1(factory, i, &adapter);
                if (hr == DxgiErrorNotFound)
                {
                    break;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    // IDXGIAdapter1 vtable: IDXGIObject(0-6), EnumOutputs 7, GetDesc 8, CheckInterfaceSupport 9, GetDesc1 10.
                    var adapterVtbl = *(nint**)adapter;
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, DxgiAdapterDesc1*, int>)adapterVtbl[10];
                    var checkInterfaceSupport = (delegate* unmanaged[Stdcall]<nint, Guid*, long*, int>)adapterVtbl[9];

                    DxgiAdapterDesc1 desc;
                    Marshal.ThrowExceptionForHR(getDesc1(adapter, &desc));
                    if ((desc.Flags & DxgiAdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    var iid = IidDxgiDevice;
                    long umdVersion;
                    var driver = checkInterfaceSupport(adapter, &iid, &umdVersion) >= 0 ? FormatDriverVersion(umdVersion) : null;

                    result.Add(new GpuInfo
                    {
                        Name = new string(desc.Description).TrimEnd('\0').Trim(),
                        Vendor = PciIds.VendorName((ushort)desc.VendorId),
                        PciVendorId = PciIds.FormatId(desc.VendorId),
                        PciDeviceId = PciIds.FormatId(desc.DeviceId),
                        PciSubsystemId = desc.SubSysId == 0 ? null : PciIds.FormatId(desc.SubSysId, 8),
                        Revision = (int)desc.Revision,
                        DedicatedMemoryBytes = (long)desc.DedicatedVideoMemory,
                        SharedMemoryBytes = (long)desc.SharedSystemMemory,
                        DriverVersion = driver,
                    });
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return result;
    }

    private static unsafe void Release(nint comObject)
    {
        var vtbl = *(nint**)comObject;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2])(comObject);
    }

    /// <summary>User-mode driver version packs four 16-bit parts, e.g. 32.0.15.6094.</summary>
    internal static string FormatDriverVersion(long value) => string.Create(CultureInfo.InvariantCulture,
        $"{(value >> 48) & 0xFFFF}.{(value >> 32) & 0xFFFF}.{(value >> 16) & 0xFFFF}.{value & 0xFFFF}");

    private sealed record DisplayClassEntry(PciHardwareId? Id, string? DriverVersion, string? DriverDate, string? BiosString, long? MemorySizeBytes)
    {
        public bool Matches(GpuInfo gpu) =>
            Id is not null
            && PciIds.FormatId(Id.VendorId) == gpu.PciVendorId
            && PciIds.FormatId(Id.DeviceId) == gpu.PciDeviceId;
    }

    [SupportedOSPlatform("windows")]
    private static List<DisplayClassEntry> ReadDisplayClass()
    {
        var entries = new List<DisplayClassEntry>();
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (root is null)
        {
            return entries;
        }

        foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
        {
            using var key = root.OpenSubKey(name);
            if (key is null)
            {
                continue;
            }

            entries.Add(new DisplayClassEntry(
                PciIds.Parse(key.GetValue("MatchingDeviceId") as string),
                key.GetValue("DriverVersion") as string,
                key.GetValue("DriverDate") as string,
                RegistryText(key.GetValue("HardwareInformation.BiosString")),
                RegistryNumber(key.GetValue("HardwareInformation.qwMemorySize")) ?? RegistryNumber(key.GetValue("HardwareInformation.MemorySize"))));
        }

        return entries;
    }

    /// <summary>Memory size may be REG_QWORD, REG_DWORD or REG_BINARY depending on the driver.</summary>
    private static long? RegistryNumber(object? value) => value switch
    {
        long l => l,
        int i => unchecked((uint)i),
        byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    /// <summary>Display drivers store some strings as REG_BINARY UTF-16.</summary>
    private static string? RegistryText(object? value)
    {
        var text = value switch
        {
            string s => s,
            byte[] b => Encoding.Unicode.GetString(b),
            _ => null,
        };

        text = text?.TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
