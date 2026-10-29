namespace HWSuite.Core.Settings;

/// <summary>
/// Where HWSuite keeps what it writes (reports, benchmark history, logo, settings).
/// Installed copies (the setup program or winget, under %LOCALAPPDATA%\Programs or Program Files) use the
/// user's Documents\HWSuite. A copy run from anywhere else — a pen drive, an unzipped download on a client's
/// machine — is portable: everything goes into an "HWSuite" folder next to the executable, so nothing is left
/// in the client's folders and the reports are easy to find. If that folder cannot be written (read-only
/// media), Documents is used after all.
/// </summary>
public static class DataLocation
{
    public const string PortableFolderName = "HWSuite";

    private static readonly Lazy<(string Folder, bool Portable)> Resolved = new(() => Resolve(AppContext.BaseDirectory));

    /// <summary>The data folder (created on first write by whoever writes).</summary>
    public static string Folder => Resolved.Value.Folder;

    /// <summary>True when running as a portable copy (data next to the executable).</summary>
    public static bool IsPortable => Resolved.Value.Portable;

    public static string DocumentsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite");

    /// <summary>Settings file: next to the data when portable, %LOCALAPPDATA%\HWSuite otherwise.</summary>
    public static string SettingsPath => IsPortable ? Path.Combine(Folder, "settings.json") : SettingsStore.DefaultPath;

    /// <summary>Whether a program in <paramref name="exeFolder"/> was installed (and so should use Documents).</summary>
    public static bool IsInstalled(string exeFolder, IEnumerable<string> installRoots)
    {
        ArgumentNullException.ThrowIfNull(exeFolder);
        ArgumentNullException.ThrowIfNull(installRoots);
        var folder = Normalize(exeFolder);
        return installRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Normalize)
            .Any(root => folder.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Program Files, %LOCALAPPDATA%\Programs (the setup's folder) and winget's package folder.</summary>
    public static IReadOnlyList<string> InstallRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            string.IsNullOrEmpty(local) ? "" : Path.Combine(local, "Programs"),
            string.IsNullOrEmpty(local) ? "" : Path.Combine(local, "Microsoft", "WinGet"),
        ];
    }

    public static (string Folder, bool Portable) Resolve(string exeFolder)
    {
        if (string.IsNullOrWhiteSpace(exeFolder) || IsInstalled(exeFolder, InstallRoots()))
        {
            return (DocumentsFolder, false);
        }

        var portable = Path.Combine(exeFolder, PortableFolderName);
        return CanWrite(portable) ? (portable, true) : (DocumentsFolder, false);
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
