using System.Buffers.Binary;
using System.Globalization;
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

    /// <summary>Build date and time, ISO 8601 ("2023-08-02T00:05:24"), from the "08/02/23,00:05:24" in the image.</summary>
    public string? BuildDate { get; init; }

    /// <summary>The board maker's boot message, e.g. "ASRock R23D-E3 SL 16GB 228W".</summary>
    public string? BoardName { get; init; }

    /// <summary>ATOM BIOS build, e.g. "022.002.001.019.000001".</summary>
    public string? AtomVersion { get; init; }

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
        var isAtom = headerOffset + 18 <= rom.Length && rom.AsSpan(headerOffset + 4, 4).SequenceEqual("ATOM"u8);

        return new VideoBiosCopy
        {
            Source = source,
            SizeBytes = rom.Length,
            ChecksumOk = checksumOk,
            IsAtomBios = isAtom,
            PartNumber = isAtom ? PartNumber(rom) : null,
            BuildDate = isAtom ? BuildDate(rom) : null,
            BoardName = isAtom ? BootMessage(rom, headerOffset) : null,
            AtomVersion = isAtom ? AtomBuild(rom) : null,
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

    /// <summary>
    /// AMD part number, located as the Linux amdgpu driver does: when byte 0x2F counts strings, the
    /// offset at 0x6E points at it; otherwise the "113-..." string in the first kilobytes.
    /// </summary>
    private static string? PartNumber(byte[] rom)
    {
        if (rom[0x2F] != 0 && BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(0x6E)) is var at && at > 0 && at < rom.Length
            && AsciiAt(rom, at, 64) is { } text && PartNumberPattern().Match(text) is { Success: true, Index: 0 } direct)
        {
            return direct.Value;
        }

        var head = Encoding.Latin1.GetString(rom, 0, Math.Min(rom.Length, 0x800));
        var match = PartNumberPattern().Match(head);
        return match.Success ? match.Value : null;
    }

    /// <summary>The build date at 0x50: "08/02/23,00:05:24" (month/day/year) or "2023/08/02 00:05".</summary>
    private static string? BuildDate(byte[] rom)
    {
        if (AsciiAt(rom, 0x50, 20) is not { } text || DatePattern().Match(text) is not { Success: true } m)
        {
            return null;
        }

        var (year, month, day) = m.Groups["y4"].Success
            ? (int.Parse(m.Groups["y4"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["m4"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["d4"].Value, CultureInfo.InvariantCulture))
            : (2000 + int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture));
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        var time = m.Groups["time"].Success ? m.Groups["time"].Value : "00:00";
        return string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}-{day:00}T{(time.Length == 5 ? time + ":00" : time)}");
    }

    /// <summary>The boot message the ATOM header points at (offset +16), without line breaks and padding.</summary>
    private static string? BootMessage(byte[] rom, int header)
    {
        var at = BinaryPrimitives.ReadUInt16LittleEndian(rom.AsSpan(header + 16));
        return at > 0 && at < rom.Length && AsciiAt(rom, at, 96)?.Trim() is { Length: > 2 } text ? text : null;
    }

    private static string? AtomBuild(byte[] rom)
    {
        var head = Encoding.Latin1.GetString(rom, 0, Math.Min(rom.Length, 0x800));
        var match = AtomVersionPattern().Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Printable ASCII from <paramref name="at"/> up to the first NUL (at most <paramref name="max"/> bytes).</summary>
    private static string? AsciiAt(byte[] rom, int at, int max)
    {
        var span = rom.AsSpan(at, Math.Min(max, rom.Length - at));
        var end = span.IndexOf((byte)0);
        span = end < 0 ? span : span[..end];
        var text = Encoding.Latin1.GetString(span).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        return text.Length > 0 && text.All(c => c is >= ' ' and <= '~') ? text : null;
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

    [GeneratedRegex(@"^(?:(?<y4>\d{4})/(?<m4>\d{2})/(?<d4>\d{2})|(?<m>\d{2})/(?<d>\d{2})/(?<y>\d{2}))(?:[ ,](?<time>\d{2}:\d{2}(?::\d{2})?))?")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"ATOMBIOSBK-AMD VER(\d+(?:\.\d+)+)")]
    private static partial Regex AtomVersionPattern();

    [GeneratedRegex(@"[^A-Za-z0-9_\-\.]+")]
    private static partial Regex Unsafe();
}
