using System.ComponentModel;
using System.Diagnostics;

namespace HWSuite.Gui.Services;

internal static class Elevation
{
    private const int ErrorCancelledByUser = 1223;

    /// <summary>Starts this app elevated through UAC. Returns false if the user declined.</summary>
    public static bool RelaunchElevated()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            });
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            return false;
        }
    }
}
