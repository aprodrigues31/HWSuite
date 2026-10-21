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
    /// <summary>
    /// The driver is still present but its package was uninstalled: Windows unloads and deletes
    /// it at the next reboot. Sensors keep working until then.
    /// </summary>
    public bool PawnIoRemovalPending { get; init; }

    public const string PawnIoInstallCommand = "hwsuite driver install";

    public static AccessStatus Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AccessStatus(false, false);
        }

        var installed = IsPawnIoInstalled();
        return new AccessStatus(IsAdministrator(), installed)
        {
            PawnIoRemovalPending = installed && !HasUninstallEntry(),
        };
    }

    /// <summary>The official installer registers "PawnIO" under Apps &amp; Features; uninstalling removes it at once.</summary>
    [SupportedOSPlatform("windows")]
    private static bool HasUninstallEntry()
    {
        string[] roots = [@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"];
        foreach (var root in roots)
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null)
            {
                continue;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                using var entry = key.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string display && display.Contains("PawnIO", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

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
