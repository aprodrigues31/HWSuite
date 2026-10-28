using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace HWSuite.Providers.Windows;

/// <summary>Win32 entry points used by the providers. All of them work without elevation.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Native
{
    public const uint RsmbSignature = 0x52534D42; // 'RSMB'
    public const uint AcpiSignature = 0x41435049; // 'ACPI'
    public const uint FileShareReadWrite = 0x1 | 0x2;
    public const uint OpenExisting = 3;
    public const uint IoctlStorageQueryProperty = 0x002D_1400;
    public const uint IoctlDiskGetDriveGeometryEx = 0x0007_00A0;
    public const int ErrorFileNotFound = 2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, byte* buffer, uint bufferSize);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte* inBuffer, uint inBufferSize, byte* outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    /// <summary>Reads a whole firmware table: first call sizes it, second call fills it.</summary>
    public static byte[] ReadFirmwareTable(uint provider, uint tableId)
    {
        var size = GetSystemFirmwareTable(provider, tableId, null, 0);
        if (size == 0)
        {
            throw new InvalidOperationException($"GetSystemFirmwareTable failed (error {Marshal.GetLastPInvokeError()}).");
        }

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            var written = GetSystemFirmwareTable(provider, tableId, p, size);
            if (written == 0 || written > size)
            {
                throw new InvalidOperationException($"GetSystemFirmwareTable failed (error {Marshal.GetLastPInvokeError()}).");
            }

            return written == size ? buffer : buffer[..(int)written];
        }
    }

    /// <summary>Issues an IOCTL; returns the filled part of the output buffer, or null on failure.</summary>
    public static byte[]? Ioctl(SafeFileHandle device, uint code, ReadOnlySpan<byte> input, int outputSize)
    {
        var output = new byte[outputSize];
        fixed (byte* inPtr = input)
        fixed (byte* outPtr = output)
        {
            if (!DeviceIoControl(device, code, inPtr, (uint)input.Length, outPtr, (uint)output.Length, out var returned, 0))
            {
                return null;
            }

            return output[..(int)Math.Min(returned, (uint)output.Length)];
        }
    }
}
