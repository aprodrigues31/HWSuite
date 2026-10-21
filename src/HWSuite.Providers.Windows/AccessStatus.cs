using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>
/// What limits low-level sensor access on this machine: elevation and the PawnIO kernel driver
/// (installed separately; HWSuite never ships a kernel driver).
/// </summary>
public sealed record AccessStatus(bool IsElevated, bool PawnIoInstalled)
{
    public const string PawnIoInstallCommand = "hwsuite driver install";

    public static AccessStatus Detect() =>
        OperatingSystem.IsWindows() ? new AccessStatus(IsAdministrator(), IsPawnIoInstalled()) : new AccessStatus(false, false);

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// The driver registers a kernel service named "PawnIO". After an uninstall Windows keeps the
    /// service key until reboot (marked with DeleteFlag) and the driver file is gone, so both are
    /// checked before calling it installed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool IsPawnIoInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        if (service is null || service.GetValue("DeleteFlag") is int and not 0)
        {
            return false;
        }

        var driverFile = ResolveDriverPath(service.GetValue("ImagePath") as string, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return driverFile is null || File.Exists(driverFile);
    }

    /// <summary>Turns a service ImagePath ("\??\C:\...", "\SystemRoot\...", "System32\...") into a file path.</summary>
    internal static string? ResolveDriverPath(string? imagePath, string windowsDirectory)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var path = imagePath.Trim().Trim('"');
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return path[4..];
        }

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(windowsDirectory, path[@"\SystemRoot\".Length..]);
        }

        if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(windowsDirectory, path);
        }

        return Path.IsPathRooted(path) ? path : null;
    }
}
