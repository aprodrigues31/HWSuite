using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace HWSuite.Gui.Services;

/// <summary>
/// Updates a portable copy (the desktop app unzipped anywhere): the verified zip is unpacked to a temporary folder,
/// and a small hidden PowerShell script waits for this process to exit, copies the new files over the app folder
/// (the HWSuite data folder next to it is not in the zip, so reports and settings stay) and starts the app again.
/// </summary>
internal static class PortableUpdater
{
    /// <summary>Whether the app folder accepts new files (a copy on read-only media cannot update itself).</summary>
    public static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".update-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Unpacks <paramref name="zip"/> and starts the script that replaces the app once it exits.</summary>
    public static void Apply(string zip, string appFolder, string exeName)
    {
        var work = Path.Combine(Path.GetTempPath(), "HWSuite-update");
        var staging = Path.Combine(work, "app");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        ZipFile.ExtractToDirectory(zip, staging);
        if (!File.Exists(Path.Combine(staging, exeName)))
        {
            throw new InvalidDataException($"{exeName} is not in the update package");
        }

        static string Q(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        var app = appFolder.TrimEnd('\\', '/');
        var script = Path.Combine(work, "apply-update.ps1");
        File.WriteAllText(script, $$"""
            # HWSuite portable update: wait for the app to close, copy the new version over it, start it again.
            $ErrorActionPreference = 'Stop'
            try { Wait-Process -Id {{Environment.ProcessId}} -Timeout 60 -ErrorAction SilentlyContinue } catch { }
            Start-Sleep -Milliseconds 500
            for ($i = 0; $i -lt 10; $i++) {
              try { Copy-Item -Path (Join-Path {{Q(staging)}} '*') -Destination {{Q(app)}} -Recurse -Force; break }
              catch { Start-Sleep -Seconds 1 }
            }
            Start-Process -FilePath (Join-Path {{Q(app)}} {{Q(exeName)}})
            Remove-Item -Path {{Q(staging)}} -Recurse -Force -ErrorAction SilentlyContinue
            """, new UTF8Encoding(true));

        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }
}
