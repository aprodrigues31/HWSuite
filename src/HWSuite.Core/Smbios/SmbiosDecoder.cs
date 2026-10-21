using System.Globalization;
using HWSuite.Core.Model;

namespace HWSuite.Core.Smbios;

/// <summary>
/// Turns SMBIOS structures into the inventory model. Offsets follow DMTF DSP0134
/// (SMBIOS Reference Specification); fields added in later versions are read only
/// when the structure is long enough to contain them.
/// </summary>
public static class SmbiosDecoder
{
    private const byte TypeBios = 0;
    private const byte TypeSystem = 1;
    private const byte TypeBaseboard = 2;
    private const byte TypeChassis = 3;
    private const byte TypeProcessor = 4;
    private const byte TypeMemoryArray = 16;
    private const byte TypeMemoryDevice = 17;

    public static void Apply(SmbiosTable table, HardwareInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(inventory);

        DecodeBios(table, inventory);
        DecodeSystem(table, inventory);
        DecodeBaseboard(table, inventory);
        DecodeProcessors(table, inventory);
        DecodeMemory(table, inventory);
    }

    private static void DecodeBios(SmbiosTable table, HardwareInventory inventory)
    {
        var bios = table.OfType(TypeBios).FirstOrDefault();
        inventory.Bios ??= new BiosInfo();
        inventory.Bios.SmbiosVersion = table.Version;
        if (bios is null)
        {
            return;
        }

        inventory.Bios.Vendor = Clean(bios, 0x04);
        inventory.Bios.Version = Clean(bios, 0x05);
        inventory.Bios.ReleaseDate = Clean(bios, 0x08);
    }

    private static void DecodeSystem(SmbiosTable table, HardwareInventory inventory)
    {
        var system = table.OfType(TypeSystem).FirstOrDefault();
        var chassis = table.OfType(TypeChassis).FirstOrDefault();
        if (system is null && chassis is null)
        {
            return;
        }

        var info = inventory.System ??= new SystemInfo();
        if (system is not null)
        {
            info.Manufacturer = Clean(system, 0x04);
            info.Product = Clean(system, 0x05);
            info.Version = Clean(system, 0x06);
            info.SerialNumber = Clean(system, 0x07);
            info.Uuid = FormatUuid(system.Bytes(0x08, 16), table.MajorVersion, table.MinorVersion);
            info.Sku = Clean(system, 0x19);
            info.Family = Clean(system, 0x1A);
        }

        if (chassis?.Byte(0x05) is { } chassisType)
        {
            info.ChassisType = ChassisTypeName(chassisType & 0x7F);
        }
    }

    private static void DecodeBaseboard(SmbiosTable table, HardwareInventory inventory)
    {
        var board = table.OfType(TypeBaseboard).FirstOrDefault();
        if (board is null)
        {
            return;
        }

        inventory.Motherboard = new MotherboardInfo
        {
            Manufacturer = Clean(board, 0x04),
            Product = Clean(board, 0x05),
            Version = Clean(board, 0x06),
            SerialNumber = Clean(board, 0x07),
        };
    }

    private static void DecodeProcessors(SmbiosTable table, HardwareInventory inventory)
    {
        var sockets = table.OfType(TypeProcessor)
            .Where(p => p.Byte(0x05) == 3) // Processor type 03h = central processor
            .Where(p => ((p.Byte(0x18) ?? 0) & 0x40) != 0) // Status bit 6 = socket populated
            .ToList();

        for (var i = 0; i < sockets.Count; i++)
        {
            var s = sockets[i];
            if (inventory.Processors.Count <= i)
            {
                inventory.Processors.Add(new ProcessorInfo());
            }

            var cpu = inventory.Processors[i];
            cpu.Socket = Clean(s, 0x04);
            cpu.Vendor ??= Clean(s, 0x07);
            cpu.Name ??= Clean(s, 0x10);
            cpu.BusClockMhz = NonZero(s.Word(0x12));
            cpu.MaxClockMhz = NonZero(s.Word(0x14));
            cpu.CurrentClockMhz = NonZero(s.Word(0x16));

            var cores = s.Byte(0x23) is 0xFF ? s.Word(0x2A) : s.Byte(0x23);
            var threads = s.Byte(0x25) is 0xFF ? s.Word(0x2E) : s.Byte(0x25);
            cpu.Cores = NonZero(cores);
            cpu.Threads = NonZero(threads);
        }
    }

    private static void DecodeMemory(SmbiosTable table, HardwareInventory inventory)
    {
        var arrays = table.OfType(TypeMemoryArray).Where(a => a.Byte(0x05) == 3).ToList(); // use 03h = system memory
        var devices = table.OfType(TypeMemoryDevice).ToList();
        if (arrays.Count == 0 && devices.Count == 0)
        {
            return;
        }

        var memory = inventory.Memory ??= new MemoryInfo();

        if (arrays.Count > 0)
        {
            memory.SlotCount = arrays.Sum(a => a.Word(0x0D) ?? 0);
            long max = 0;
            foreach (var array in arrays)
            {
                var kib = array.DWord(0x07) ?? 0;
                max += kib == 0x8000_0000 ? (long)(array.QWord(0x0F) ?? 0) : kib * 1024L;
            }

            memory.MaxCapacityBytes = max > 0 ? max : null;
        }

        foreach (var device in devices)
        {
            var size = MemoryDeviceSizeBytes(device);
            if (size <= 0)
            {
                continue; // empty slot
            }

            var speed = device.Word(0x15) is 0xFFFF ? (int?)device.DWord(0x54) : device.Word(0x15);
            var configured = device.Word(0x20) is 0xFFFF ? (int?)device.DWord(0x58) : device.Word(0x20);
            var ranks = (device.Byte(0x1B) ?? 0) & 0x0F;

            memory.Modules.Add(new MemoryModule
            {
                Slot = Clean(device, 0x10),
                Bank = Clean(device, 0x11),
                SizeBytes = size,
                FormFactor = FormFactorName(device.Byte(0x0E) ?? 0),
                Type = MemoryTypeName(device.Byte(0x12) ?? 0),
                SpeedMts = NonZero(speed),
                ConfiguredSpeedMts = NonZero(configured),
                Ranks = ranks == 0 ? null : ranks,
                Manufacturer = Clean(device, 0x17),
                SerialNumber = Clean(device, 0x18),
                PartNumber = Clean(device, 0x1A),
            });
        }
    }

    internal static long MemoryDeviceSizeBytes(SmbiosStructure device)
    {
        var size = device.Word(0x0C) ?? 0;
        if (size is 0 or 0xFFFF)
        {
            return 0;
        }

        if (size == 0x7FFF)
        {
            // Extended Size (2.7+): bits 0-30 in MiB.
            return ((device.DWord(0x1C) ?? 0) & 0x7FFF_FFFF) * 1024L * 1024L;
        }

        // Bit 15: 0 = MiB, 1 = KiB.
        return (size & 0x8000) == 0 ? size * 1024L * 1024L : (size & 0x7FFF) * 1024L;
    }

    /// <summary>
    /// SMBIOS 2.6+ stores the first three UUID fields little-endian (RFC 4122 wire order differs).
    /// All-zero or all-FF means "not present / not set".
    /// </summary>
    internal static string? FormatUuid(ReadOnlySpan<byte> b, int major, int minor)
    {
        if (b.Length != 16 || !b.ContainsAnyExcept((byte)0x00) || !b.ContainsAnyExcept((byte)0xFF))
        {
            return null;
        }

        var littleEndian = major > 2 || (major == 2 && minor >= 6);
        var guid = littleEndian ? new Guid(b) : new Guid(b, bigEndian: true);
        return guid.ToString("D").ToUpperInvariant();
    }

    internal static string? MemoryTypeName(byte code) => code switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x1F => "Logical non-volatile device",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        0x00 or 0x01 or 0x02 => null,
        _ => string.Create(CultureInfo.InvariantCulture, $"0x{code:X2}"),
    };

    internal static string? FormFactorName(byte code) => code switch
    {
        0x09 => "DIMM",
        0x0B => "Row of chips",
        0x0D => "SODIMM",
        0x0F => "FB-DIMM",
        0x10 => "Die",
        0x11 => "CAMM",
        _ => null,
    };

    internal static string? ChassisTypeName(int code) => code switch
    {
        3 => "Desktop",
        4 => "Low Profile Desktop",
        5 => "Pizza Box",
        6 => "Mini Tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Hand Held",
        13 => "All in One",
        14 => "Sub Notebook",
        15 => "Space-saving",
        16 => "Lunch Box",
        17 => "Main Server Chassis",
        23 => "Rack Mount Chassis",
        24 => "Sealed-case PC",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        33 => "IoT Gateway",
        34 => "Embedded PC",
        35 => "Mini PC",
        36 => "Stick PC",
        _ => null,
    };

    private static string? Clean(SmbiosStructure structure, int offset) => CleanPlaceholder(structure.Text(offset));

    private static int? NonZero(int? value) => value is null or 0 ? null : value;

    private static readonly string[] Placeholders =
    [
        "Unknown", "Not Specified", "Not Applicable", "To Be Filled By O.E.M.", "To be filled by O.E.M.", "Default string",
        "None", "N/A", "0000000000000000", "System Product Name", "System manufacturer", "System Version",
        "System Serial Number", "Base Board Serial Number", "Type2 - Board Serial Number", "123456789", "O.E.M.",
    ];

    private static string? CleanPlaceholder(string? value) =>
        value is null || Placeholders.Any(p => p.Equals(value, StringComparison.OrdinalIgnoreCase)) ? null : value;
}
