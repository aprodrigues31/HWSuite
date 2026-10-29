namespace HWSuite.Core.Maintenance;

/// <summary>The maintenance tasks HWSuite can run on Windows.</summary>
[Flags]
public enum TuneupTasks
{
    None = 0,

    /// <summary>Windows Disk Cleanup handlers (temporary files, update leftovers, caches, error reports).</summary>
    Cleanup = 1,

    /// <summary>"Optimize drives": TRIM on SSDs, defragmentation on hard disks.</summary>
    Optimize = 2,

    /// <summary>Search, download and install Windows updates.</summary>
    WindowsUpdate = 4,

    /// <summary>Update Microsoft Store apps.</summary>
    StoreApps = 8,

    /// <summary>Update installed programs through winget (desktop apps).</summary>
    DesktopApps = 16,

    Default = Cleanup | Optimize | WindowsUpdate | StoreApps,
}

/// <summary>Options of the disk cleanup.</summary>
public sealed record CleanupOptions(bool EmptyRecycleBin = false, bool RemovePreviousWindows = false);

/// <summary>How one task went.</summary>
/// <param name="Summary">One line for the report's "services performed" ("Disk cleanup: 3.2 GB freed").</param>
/// <param name="Details">Longer text for the screen (updates installed, drives optimized...).</param>
public sealed record TuneupOutcome(TuneupTasks Task, bool Succeeded, string Summary, IReadOnlyList<string> Details)
{
    public bool RebootRequired { get; init; }
}

/// <summary>One Windows update found (and what happened to it).</summary>
public sealed record WindowsUpdateItem(string Title, string? Kb, long? SizeBytes, string? Result);

/// <summary>Text helpers for the maintenance tasks (shared by the app and the command line).</summary>
public static class Tuneup
{
    /// <summary>
    /// Disk Cleanup handlers run by default (Explorer\VolumeCaches key names). "Downloads" is never on the list: the
    /// user's downloads folder is not HWSuite's to empty. The recycle bin and the previous Windows installation (which
    /// allows going back to it) only when asked.
    /// </summary>
    public static IReadOnlyList<string> CleanupHandlers(CleanupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> handlers =
        [
            "Active Setup Temp Folders", "BranchCache", "D3D Shader Cache", "Delivery Optimization Files", "Device Driver Packages",
            "Diagnostic Data Viewer database files", "Downloaded Program Files", "Internet Cache Files", "Language Pack",
            "Old ChkDsk Files", "RetailDemo Offline Content", "Setup Log Files", "System error memory dump files",
            "System error minidump files", "Temporary Files", "Temporary Setup Files", "Thumbnail Cache", "Update Cleanup",
            "Upgrade Discarded Files", "User file versions", "Windows Defender", "Windows Error Reporting Files",
            "Windows ESD installation files", "Windows Upgrade Log Files",
        ];
        if (options.EmptyRecycleBin)
        {
            handlers.Add("Recycle Bin");
        }

        if (options.RemovePreviousWindows)
        {
            handlers.Add("Previous Installations");
        }

        return handlers;
    }

    /// <summary>"KB5044284" out of an update title, or null.</summary>
    public static string? KbOf(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var match = System.Text.RegularExpressions.Regex.Match(title, @"KB\d{6,8}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Packages winget upgraded, from its output ("Successfully installed" lines follow each "Installing ..." / "Found ..."
    /// line). Counts both English and Portuguese outputs.
    /// </summary>
    public static int WingetInstalled(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split('\n').Count(line =>
            line.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Instalado com êxito", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Instalação bem-sucedida", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"No installed package found matching" / "No available upgrade found" / "Nenhuma atualização disponível".</summary>
    public static bool WingetNothingToDo(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Contains("No available upgrade", StringComparison.OrdinalIgnoreCase)
            || output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Nenhuma atualização disponível", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Nenhum pacote instalado", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Windows Update result codes (OperationResultCode).</summary>
    public static string ResultName(int code) => code switch
    {
        0 => "NotStarted",
        1 => "InProgress",
        2 => "Succeeded",
        3 => "SucceededWithErrors",
        4 => "Failed",
        5 => "Aborted",
        _ => code.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
