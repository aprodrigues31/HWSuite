using System.Buffers.Binary;
using System.Text;

namespace HWSuite.Core.Smbios;

/// <summary>One SMBIOS structure: formatted area plus its string set.</summary>
public sealed class SmbiosStructure
{
    private readonly byte[] _formatted;
    private readonly IReadOnlyList<string> _strings;

    internal SmbiosStructure(byte[] formatted, IReadOnlyList<string> strings)
    {
        _formatted = formatted;
        _strings = strings;
    }

    public byte Type => _formatted[0];

    /// <summary>Length of the formatted area, including the 4-byte header.</summary>
    public int Length => _formatted.Length;

    public ushort Handle => BinaryPrimitives.ReadUInt16LittleEndian(_formatted.AsSpan(2));

    public bool Has(int offset, int size) => offset + size <= _formatted.Length;

    public byte? Byte(int offset) => Has(offset, 1) ? _formatted[offset] : null;

    public ushort? Word(int offset) =>
        Has(offset, 2) ? BinaryPrimitives.ReadUInt16LittleEndian(_formatted.AsSpan(offset)) : null;

    public uint? DWord(int offset) =>
        Has(offset, 4) ? BinaryPrimitives.ReadUInt32LittleEndian(_formatted.AsSpan(offset)) : null;

    public ulong? QWord(int offset) =>
        Has(offset, 8) ? BinaryPrimitives.ReadUInt64LittleEndian(_formatted.AsSpan(offset)) : null;

    public ReadOnlySpan<byte> Bytes(int offset, int size) =>
        Has(offset, size) ? _formatted.AsSpan(offset, size) : [];

    /// <summary>Resolves the string whose 1-based index is stored at <paramref name="offset"/>.</summary>
    public string? Text(int offset)
    {
        var index = Byte(offset);
        if (index is null or 0 || index > _strings.Count)
        {
            return null;
        }

        var value = _strings[index.Value - 1].Trim();
        return value.Length == 0 ? null : value;
    }
}

/// <summary>Parsed SMBIOS table, independent of where the raw bytes came from.</summary>
public sealed class SmbiosTable
{
    public SmbiosTable(int majorVersion, int minorVersion, IReadOnlyList<SmbiosStructure> structures)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        Structures = structures;
    }

    public int MajorVersion { get; }

    public int MinorVersion { get; }

    public IReadOnlyList<SmbiosStructure> Structures { get; }

    public string Version => $"{MajorVersion}.{MinorVersion}";

    public IEnumerable<SmbiosStructure> OfType(byte type) => Structures.Where(s => s.Type == type);

    /// <summary>
    /// Parses the payload returned by Windows GetSystemFirmwareTable('RSMB'):
    /// an 8-byte RawSMBIOSData header followed by the structure table.
    /// </summary>
    public static SmbiosTable ParseWindowsRaw(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 8)
        {
            throw new FormatException("RawSMBIOSData is shorter than its 8-byte header.");
        }

        int major = raw[1];
        int minor = raw[2];
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
        var available = Math.Min(length, raw.Length - 8);
        return new SmbiosTable(major, minor, ParseStructures(raw.Slice(8, available)));
    }

    /// <summary>Parses a bare structure table (e.g. Linux /sys/firmware/dmi/tables/DMI).</summary>
    public static SmbiosTable ParseTable(int major, int minor, ReadOnlySpan<byte> table) =>
        new(major, minor, ParseStructures(table));

    private static List<SmbiosStructure> ParseStructures(ReadOnlySpan<byte> table)
    {
        var result = new List<SmbiosStructure>();
        var position = 0;

        while (position + 4 <= table.Length)
        {
            var type = table[position];
            int length = table[position + 1];
            if (length < 4 || position + length > table.Length)
            {
                break;
            }

            var formatted = table.Slice(position, length).ToArray();

            // The string set follows the formatted area and ends with a double NUL.
            var stringsStart = position + length;
            var cursor = stringsStart;
            var strings = new List<string>();
            while (cursor < table.Length)
            {
                var end = table[cursor..].IndexOf((byte)0);
                if (end < 0)
                {
                    cursor = table.Length;
                    break;
                }

                if (end == 0)
                {
                    // Empty string = end of set. A structure with no strings has two NULs.
                    cursor += cursor == stringsStart ? 2 : 1;
                    break;
                }

                strings.Add(Encoding.ASCII.GetString(table.Slice(cursor, end)));
                cursor += end + 1;
            }

            result.Add(new SmbiosStructure(formatted, strings));
            position = cursor;

            if (type == 127)
            {
                break;
            }
        }

        return result;
    }
}
