using System.Runtime.InteropServices;
using System.Text;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Protects saved passwords with Windows DPAPI (CryptProtectData): only this Windows user on this computer can open
/// them again, so a copied settings file does not give the passwords away. Stored as "dpapi:" + Base64.
/// </summary>
public static unsafe partial class Secrets
{
    private const string Prefix = "dpapi:";
    private const uint UiForbidden = 0x1;

    public static string? Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Password protection needs Windows");
        }

        var bytes = Encoding.UTF8.GetBytes(secret);
        fixed (byte* p = bytes)
        {
            var input = new Blob { Size = (uint)bytes.Length, Data = (nint)p };
            if (!CryptProtectData(ref input, null, 0, 0, 0, UiForbidden, out var output))
            {
                throw new InvalidOperationException("CryptProtectData failed: " + Marshal.GetLastPInvokeError());
            }

            try
            {
                return Prefix + Convert.ToBase64String(new ReadOnlySpan<byte>((void*)output.Data, (int)output.Size));
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
    }

    /// <summary>The secret, or null when there is none or it cannot be opened here (another user or computer).</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }

        fixed (byte* p = bytes)
        {
            var input = new Blob { Size = (uint)bytes.Length, Data = (nint)p };
            if (!CryptUnprotectData(ref input, 0, 0, 0, 0, UiForbidden, out var output))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)output.Data, (int)output.Size));
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public uint Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref Blob dataIn, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref Blob dataIn, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
