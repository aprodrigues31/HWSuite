using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HWSuite.Core.Gpu;

/// <summary>One video BIOS image from the ACPI VFCT table, with the PCI ids of the card it belongs to.</summary>
public sealed record VfctImage(int Bus, int Device, int Function, ushort VendorId, ushort DeviceId, ushort SubsystemVendorId, ushort SubsystemId, byte[] Rom);

/// <summary>What the inventory keeps of a card's video BIOS: a description, and the image for saving.</summary>
public sealed class VideoBiosCopy
{
    /// <summary>Where the copy came from ("ACPI VFCT").</summary>
    public required string Source { get; init; }

    public required int SizeBytes { get; init; }

    /// <summary>The PCI option ROM checksum (all bytes of the image add up to 0) holds.</summary>
    public required bool ChecksumOk { get; init; }

    /// <summary>The image carries AMD ATOM BIOS tables.</summary>
    public required bool IsAtomBios { get; init; }

    /// <summary>AMD part number, e.g. "113-APM6766SL-100".</summary>
    public string? PartNumber { get; init; }

    /// <summary>Build date as written in the image, e.g. "2023/07/13 01:28".</summary>
    public string? BuildDate { get; init; }

    /// <summary>The image itself; left out of the JSON report.</summary>
    [JsonIgnore]
    public byte[] Image { get; init; } = [];
}

/// <summary>
/// The video BIOS as UEFI firmware hands it to the operating system: when an AMD card initializes the
/// boot display, the firmware copies its option ROM into the ACPI "VFCT" table (the copy the Linux amdgpu
/// driver loads too). Reading an ACPI table needs no driver and no administrator rights. NVIDIA and Intel
/// cards have no such table.
/// </summary>
public static partial class VideoBios
{
    private const int AcpiHeaderSize = 36;
    private const int VfctHeaderSize = AcpiHeaderSize + 16 + 4 + 4 + 16; // UUID, VBIOS offset, Lib1 offset, reserved
    private const int ImageHeaderSize = 28;

    /// <summary>The images in a VFCT table (UEFI_ACPI_VFCT followed by VFCT_IMAGE_HEADER + image, repeated).</summary>
    public static IReadOnlyList<VfctImage> ParseVfct(ReadOnlySpan<byte> table)
    {
        var images = new List<VfctImage>();
        if (table.Length < VfctHeaderSize || !table[..4].SequenceEqual("VFCT"u8))
        {
            return images;
        }

        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(table[4..]), (uint)table.Length);
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(table[(AcpiHeaderSize + 16)..]);
        while (offset >= VfctHeaderSize && offset + ImageHeaderSize <= length)
        {
            var h = table.Slice(offset, ImageHeaderSize);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
            if (size == 0 || offset + ImageHeaderSize + size > length)
            {
                break;
            }

            images.Add(new VfctImage(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(h),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(h[4..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(h[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[12..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[14..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[18..]),
                table.Slice(offset + ImageHeaderSize, (int)size).ToArray()));
            offset += ImageHeaderSize + (int)size;
        }

        return images;
    }

    /// <summary>Checks and describes an option ROM image; null when it is not one (no 55 AA signature).</summary>
    public static VideoBiosCopy? Describe(byte[] rom, string source)
    {
        ArgumentNullException.ThrowIfNull(rom);
        if (rom.Length < 0x100 || rom[0] != 0x55 || rom[1] != 0xAA)
        {
            return null;
        }

        // The legacy image length is byte 2 in 512-byte blocks; its bytes add up to 0 (mod 256).
        var declared = rom[2] * 512;
        var checksumOk = declared > 0 && declared <= rom.Length && Sum(rom.AsSpan(0, declared)) == 0;

        var headerOffset = BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(0x48));
        var isAtom = headerOffset + 8 <= rom.Length && rom.AsSpan(headerOffset + 4, 4).SequenceEqual("ATOM"u8);

        return new VideoBiosCopy
        {
            Source = source,
            SizeBytes = rom.Length,
            ChecksumOk = checksumOk,
            IsAtomBios = isAtom,
            PartNumber = isAtom ? PartNumber(rom) : null,
            BuildDate = isAtom ? BuildDate(rom) : null,
            Image = rom,
        };
    }

    /// <summary>"RX-7800-XT-113-APM6766SL-100.rom": a file name that says which card and BIOS it is.</summary>
    public static string FileName(string? model, VideoBiosCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        var name = string.Join("-", new[] { model, copy.PartNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));
        name = Unsafe().Replace(name.Length > 0 ? name : "vbios", "-").Trim('-');
        return name + ".rom";
    }

    /// <summary>AMD part numbers ("113-...") sit in the first kilobytes of the image, as an ASCII string.</summary>
    private static string? PartNumber(byte[] rom)
    {
        var text = Encoding.Latin1.GetString(rom, 0, Math.Min(rom.Length, 0x800));
        var match = PartNumberPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    /// <summary>The ATOM image keeps its build date at 0x50 ("2023/07/13 01:28" or "07/13/23 01:28").</summary>
    private static string? BuildDate(byte[] rom)
    {
        if (rom.Length < 0x60)
        {
            return null;
        }

        var text = Encoding.Latin1.GetString(rom, 0x50, 16);
        var match = DatePattern().Match(text);
        return match.Success ? match.Value.Trim() : null;
    }

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (var b in bytes)
        {
            sum += b;
        }

        return sum;
    }

    [GeneratedRegex(@"113-[A-Z0-9][A-Z0-9_\-\.]{4,30}")]
    private static partial Regex PartNumberPattern();

    [GeneratedRegex(@"^(\d{4}/\d{2}/\d{2}|\d{2}/\d{2}/\d{2})( \d{2}:\d{2})?")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"[^A-Za-z0-9_\-\.]+")]
    private static partial Regex Unsafe();
}
