using HWSuite.Core.Settings;

namespace HWSuite.Gui.Services;

/// <summary>
/// Builds the technician's pen drive: a copy of this HWSuite (desktop app and command line) in a folder of the
/// user's choice, with an "HWSuite" data folder beside it holding the company profile, the logo and a Reports
/// folder, all stored relative to the copy so it works whatever letter the drive gets on a client's computer.
/// </summary>
internal static class PortableKit
{
    public const string FolderName = "HWSuite Portatil";

    /// <summary>Files and folders of this copy that are not part of the program (data, browser cache, uninstaller).</summary>
    private static bool Skip(string relative)
    {
        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals(DataLocation.PortableFolderName, StringComparison.OrdinalIgnoreCase)
            || first.EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase)
            || first.StartsWith("unins", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Copies the program to <paramref name="target"/>\HWSuite Portatil and returns that folder.</summary>
    public static string Create(string target, AppSettings settings, string? logoFile, IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var source = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var root = Path.Combine(target, FolderName);
        if (string.Equals(Path.GetFullPath(root).TrimEnd('\\'), source, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(App.Text["kit.sameFolder"]);
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(source, f)))
            .Where(f => !Skip(f.Relative))
            .ToList();
        for (var i = 0; i < files.Count; i++)
        {
            var destination = Path.Combine(root, files[i].Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(files[i].Full, destination, overwrite: true);
            progress?.Report((i + 1) / (double)files.Count);
        }

        // The data folder: settings with the company, the logo, an empty Reports folder; paths relative to the copy.
        var data = Path.Combine(root, DataLocation.PortableFolderName);
        Directory.CreateDirectory(Path.Combine(data, "Reports"));
        string? logo = settings.LogoFile is "" ? "" : null;
        if (logoFile is { Length: > 0 } && File.Exists(logoFile))
        {
            var name = "logo" + Path.GetExtension(logoFile).ToLowerInvariant();
            File.Copy(logoFile, Path.Combine(data, name), overwrite: true);
            logo = Path.Combine(DataLocation.PortableFolderName, name);
        }

        var portable = settings with
        {
            ReportsFolder = Path.Combine(DataLocation.PortableFolderName, "Reports"),
            LogoFile = logo,
            LogFolder = null, // HWSuite\logs next to the copy
            LogEnabled = false,
            StartWithWindows = false,
            PumpSensorId = null, // each client's computer has its own fans
            SensorCategories = null,
            InventoryCategories = null,
            ChartSensors = null,
        };
        new SettingsStore(Path.Combine(data, "settings.json")).Save(portable);
        return root;
    }
}
