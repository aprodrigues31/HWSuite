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
    public const string PawnIoInstallCommand = "winget install namazso.PawnIO";

    public static AccessStatus Detect() =>
        OperatingSystem.IsWindows() ? new AccessStatus(IsAdministrator(), IsPawnIoInstalled()) : new AccessStatus(false, false);

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>The driver registers a kernel service named "PawnIO".</summary>
    [SupportedOSPlatform("windows")]
    private static bool IsPawnIoInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return service is not null;
    }
}
