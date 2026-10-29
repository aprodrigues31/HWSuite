using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace HWSuite.Providers.Windows;

/// <summary>A drive the disk benchmark can test: letter, the physical disk behind it, size and free space.</summary>
public sealed record DiskBenchmarkDrive(string Letter, string? Label, string? Model, string? Bus, long TotalBytes, long FreeBytes);

/// <summary>
/// Windows side of the disk benchmark: the drives to offer (with the disk model behind each letter, from
/// IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS), where the test file goes, and the unbuffered file opener.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class DiskBenchmarkTargets
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenAlways = 4;
    private const uint FlagNoBuffering = 0x20000000;
    private const uint FlagWriteThrough = 0x80000000;
    private const uint IoctlVolumeGetDiskExtents = 0x00560000;

    /// <summary>Fixed and removable drives that are ready, with their disk model when Windows tells it.</summary>
    public static IReadOnlyList<DiskBenchmarkDrive> Drives()
    {
        // The disks (index -> model, bus) from the storage provider alone; it reports its own problems as diagnostics.
        var disks = new Core.Collection.InventoryCollector([new WindowsStorageProvider()]).Collect();

        var result = new List<DiskBenchmarkDrive>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
            {
                continue;
            }

            var letter = drive.Name.TrimEnd('\\');
            var disk = DiskNumber(letter) is { } number ? disks.Disks.FirstOrDefault(d => d.Index == number) : null;
            result.Add(new DiskBenchmarkDrive(letter, string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel, disk?.Model, disk?.BusType, drive.TotalSize, drive.AvailableFreeSpace));
        }

        return result;
    }

    /// <summary>
    /// Folder for the test file: the user's temporary folder on the Windows drive (the root needs administrator
    /// rights there), a HWSuite-bench folder on other drives.
    /// </summary>
    public static string TestFolder(string letter)
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        var folder = string.Equals(system, letter, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetTempPath(), "HWSuite-bench")
            : Path.Combine(letter + "\\", "HWSuite-bench");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Opens (creating if needed) the test file, bypassing the Windows cache in both directions.</summary>
    public static SafeFileHandle Open(string path, bool write)
    {
        var handle = CreateFileW(path, GenericRead | GenericWrite, Native.FileShareReadWrite, 0, OpenAlways, FlagNoBuffering | FlagWriteThrough, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"Cannot open {path} for the disk test (error {error}).");
        }

        return handle;
    }

    /// <summary>The physical disk number a drive letter lives on (first extent), or null.</summary>
    private static int? DiskNumber(string letter)
    {
        using var volume = Native.CreateFile($@"\\.\{letter}", 0, Native.FileShareReadWrite, 0, Native.OpenExisting, 0, 0);
        if (volume.IsInvalid)
        {
            return null;
        }

        // VOLUME_DISK_EXTENTS { DWORD count; (padding); DISK_EXTENT { DWORD disk; LARGE_INTEGER start, length } [] }
        return Native.Ioctl(volume, IoctlVolumeGetDiskExtents, [], 64) is { Length: >= 32 } extents && BitConverter.ToInt32(extents, 0) >= 1
            ? BitConverter.ToInt32(extents, 8)
            : null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
