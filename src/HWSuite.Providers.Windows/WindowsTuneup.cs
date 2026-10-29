using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Reporting;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Windows maintenance with Windows' own tools: Disk Cleanup handlers (cleanmgr), "Optimize drives" (defrag /O: TRIM
/// on SSDs, defragmentation on hard disks), the Windows Update agent and winget / the Microsoft Store for apps. Every
/// task needs administrator rights; each reports a one-line summary for the maintenance report and the details.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsTuneup
{
    private const int SageSet = 731; // StateFlags0731: HWSuite's own Disk Cleanup preset, removed afterwards
    private const string VolumeCaches = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    // ---- Disk cleanup -----------------------------------------------------------------------------------------------

    /// <summary>The Windows drive ("C:").</summary>
    public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');

    public static TuneupOutcome Cleanup(CleanupOptions options, Strings s, Action<string>? log = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(s);
        var system = SystemDrive;
        var drives = (options.Drives is { Count: > 0 } chosen ? chosen : [system])
            .Select(d => Tuneup.DriveLetter(d))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var before = drives.ToDictionary(d => d, FreeBytes, StringComparer.Ordinal);
        var details = new List<string>();

        // Windows' own cleanup acts on the Windows drive: only when that drive was chosen.
        if (Tuneup.Includes(drives, system))
        {
            WindowsCleanup(options, s, details, log, cancel);
        }

        foreach (var drive in drives)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            if (options.EmptyRecycleBin)
            {
                log?.Invoke(s.Format("tune.cleanup.recycleDrive", drive));
                var result = SHEmptyRecycleBinW(0, drive + "\\", EmptyNoConfirmation | EmptyNoProgressUi | EmptyNoSound);
                details.Add(result is 0 or unchecked((int)0x8000FFFF) // E_UNEXPECTED: the bin was already empty
                    ? s.Format("tune.cleanup.recycled", drive)
                    : s.Format("tune.cleanup.recycleFailed", drive, result.ToString("X8", CultureInfo.InvariantCulture)));
            }

            var chk = DeleteChkdskLeftovers(drive, cancel);
            if (chk > 0)
            {
                details.Add(s.Format("tune.cleanup.chkdsk", drive, chk));
            }
        }

        long total = 0;
        foreach (var drive in drives)
        {
            var freed = Math.Max(0, FreeBytes(drive) - before[drive]);
            total += freed;
            details.Add(s.Format("tune.cleanup.driveFreed", drive, ReportModel.Bytes(freed, s.Culture)));
        }

        var summary = s.Format("tune.cleanup.summary", ReportModel.Bytes(total, s.Culture)) + " (" + string.Join(", ", drives) + ")";
        return new TuneupOutcome(TuneupTasks.Cleanup, true, summary, details);
    }

    /// <summary>Disk Cleanup handlers (cleanmgr /sagerun with HWSuite's own preset) and the temporary folders.</summary>
    private static void WindowsCleanup(CleanupOptions options, Strings s, List<string> details, Action<string>? log, CancellationToken cancel)
    {
        var flag = string.Create(CultureInfo.InvariantCulture, $"StateFlags{SageSet:0000}");
        var marked = new List<string>();
        try
        {
            using var caches = Registry.LocalMachine.OpenSubKey(VolumeCaches, writable: true);
            foreach (var handler in Tuneup.CleanupHandlers(options))
            {
                using var key = caches?.OpenSubKey(handler, writable: true);
                if (key is not null)
                {
                    key.SetValue(flag, 2, RegistryValueKind.DWord);
                    marked.Add(handler);
                }
            }

            var cleanmgr = Path.Combine(Environment.SystemDirectory, "cleanmgr.exe");
            if (marked.Count > 0 && File.Exists(cleanmgr))
            {
                log?.Invoke(s["tune.cleanup.running"]);
                Run(cleanmgr, string.Create(CultureInfo.InvariantCulture, $"/sagerun:{SageSet}"), TimeSpan.FromHours(1), null, null, cancel);
                details.Add(s.Format("tune.cleanup.handlers", marked.Count));
            }
            else
            {
                details.Add(s["tune.cleanup.noCleanmgr"]);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            details.Add(ex.Message);
        }
        finally
        {
            // Leave the Disk Cleanup settings as they were.
            try
            {
                using var caches = Registry.LocalMachine.OpenSubKey(VolumeCaches, writable: true);
                foreach (var handler in marked)
                {
                    using var key = caches?.OpenSubKey(handler, writable: true);
                    key?.DeleteValue(flag, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // nothing more to do
            }
        }

        // Temporary folders: files older than a day (Disk Cleanup keeps a week's worth).
        log?.Invoke(s["tune.cleanup.temp"]);
        var removed = DeleteOld(Path.GetTempPath(), TimeSpan.FromDays(1), cancel)
            + DeleteOld(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), TimeSpan.FromDays(1), cancel);
        details.Add(s.Format("tune.cleanup.tempFiles", removed.ToString("N0", s.Culture)));
    }

    private static long FreeBytes(string drive)
    {
        try
        {
            var info = new DriveInfo(drive);
            return info.IsReady ? info.AvailableFreeSpace : 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Files CHKDSK recovered into X:\FOUND.000, FOUND.001...: *.chk files, and the folder when it ends up empty.</summary>
    private static int DeleteChkdskLeftovers(string drive, CancellationToken cancel)
    {
        var count = 0;
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(drive + "\\", "FOUND.*")
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^FOUND\.\d{3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var folder in folders)
        {
            List<string> files;
            try
            {
                files = [.. Directory.EnumerateFiles(folder, "*.chk")];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (cancel.IsCancellationRequested)
                {
                    return count;
                }

                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // stays
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // stays
            }
        }

        return count;
    }

    private const uint EmptyNoConfirmation = 0x1;
    private const uint EmptyNoProgressUi = 0x2;
    private const uint EmptyNoSound = 0x4;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint window, string rootPath, uint flags);

    private static int DeleteOld(string folder, TimeSpan age, CancellationToken cancel)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var limit = DateTime.Now - age;
        var count = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                    count++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use: stays
            }
        }

        return count;
    }

    // ---- Optimize drives --------------------------------------------------------------------------------------------

    /// <summary>Runs "Optimize drives" on <paramref name="drives"/> ("C:", "D:"); null = every fixed drive.</summary>
    public static TuneupOutcome Optimize(IReadOnlyList<string>? drives, Strings s, Action<string>? log = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        var defrag = Path.Combine(Environment.SystemDirectory, "defrag.exe");
        var details = new List<string>();
        var letters = drives is { Count: > 0 }
            ? drives.Select(d => Tuneup.DriveLetter(d)).OfType<string>().Distinct(StringComparer.Ordinal).ToList()
            : DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name.TrimEnd('\\')).ToList();
        var done = 0;
        foreach (var letter in letters)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            log?.Invoke(s.Format("tune.optimize.drive", letter));
            var (code, _) = Run(defrag, $"{letter} /O /H", TimeSpan.FromHours(3), null, null, cancel);
            if (code == 0)
            {
                done++;
                details.Add(s.Format("tune.optimize.ok", letter));
            }
            else
            {
                details.Add(s.Format("tune.optimize.failed", letter, code));
            }
        }

        var summary = s.Format("tune.optimize.summary", done) + (letters.Count > 0 ? " (" + string.Join(", ", letters) + ")" : "");
        return new TuneupOutcome(TuneupTasks.Optimize, done > 0, summary, details);
    }

    // ---- Windows Update ---------------------------------------------------------------------------------------------

    /// <summary>Searches, downloads and installs software updates through the Windows Update agent (COM).</summary>
    public static TuneupOutcome WindowsUpdate(Strings s, Action<string>? log = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        TuneupOutcome? outcome = null;
        Exception? failure = null;

        // The agent's objects want a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try
            {
                outcome = UpdateAgent(s, log, cancel);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
                or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException or InvalidOperationException)
            {
                failure = ex;
            }
        })
        { IsBackground = true, Name = "HWSuite Windows Update" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return outcome ?? new TuneupOutcome(TuneupTasks.WindowsUpdate, false,
            s.Format("tune.wu.failed", failure?.Message ?? "?"), [failure?.Message ?? "?"]);
    }

    private static TuneupOutcome UpdateAgent(Strings s, Action<string>? log, CancellationToken cancel)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new InvalidOperationException("Windows Update agent not found");
        dynamic session = Activator.CreateInstance(sessionType)!;
        session.ClientApplicationID = "HWSuite";
        log?.Invoke(s["tune.wu.searching"]);
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
        dynamic updates = found.Updates;
        int count = updates.Count;
        if (count == 0)
        {
            return new TuneupOutcome(TuneupTasks.WindowsUpdate, true, s["tune.wu.none"], [s["tune.wu.none"]]);
        }

        var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl") ?? throw new InvalidOperationException("UpdateColl");
        dynamic batch = Activator.CreateInstance(collectionType)!;
        var titles = new List<string>();
        for (var i = 0; i < count; i++)
        {
            dynamic update = updates[i];
            if ((bool)update.InstallationBehavior.CanRequestUserInput)
            {
                continue; // needs someone at the screen: left for Windows Update itself
            }

            if (!(bool)update.EulaAccepted)
            {
                update.AcceptEula();
            }

            batch.Add(update);
            titles.Add((string)update.Title);
        }

        if (titles.Count == 0 || cancel.IsCancellationRequested)
        {
            return new TuneupOutcome(TuneupTasks.WindowsUpdate, true, s.Format("tune.wu.pending", count), titles);
        }

        log?.Invoke(s.Format("tune.wu.downloading", titles.Count));
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = batch;
        downloader.Download();
        if (cancel.IsCancellationRequested)
        {
            return new TuneupOutcome(TuneupTasks.WindowsUpdate, true, s.Format("tune.wu.pending", titles.Count), titles);
        }

        log?.Invoke(s.Format("tune.wu.installing", titles.Count));
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = batch;
        dynamic result = installer.Install();
        var details = new List<string>();
        var installed = 0;
        for (var i = 0; i < titles.Count; i++)
        {
            int code = result.GetUpdateResult(i).ResultCode;
            installed += code is 2 or 3 ? 1 : 0;
            details.Add($"{titles[i]} — {s[$"tune.wu.result.{Tuneup.ResultName(code)}"]}");
        }

        bool reboot = result.RebootRequired;
        var summary = s.Format("tune.wu.summary", installed, titles.Count) + (reboot ? " · " + s["tune.reboot"] : "");
        return new TuneupOutcome(TuneupTasks.WindowsUpdate, installed == titles.Count, summary, details) { RebootRequired = reboot };
    }

    // ---- Apps -------------------------------------------------------------------------------------------------------

    /// <summary>Microsoft Store apps: winget's "msstore" source, plus the Store's own update scan.</summary>
    public static TuneupOutcome StoreApps(Strings s, Action<string>? log = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        var details = new List<string>();
        log?.Invoke(s["tune.store.scan"]);

        // Asks the Store to look for and install updates of its apps (the "Get updates" button).
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        const string Scan = "Get-CimInstance -Namespace 'Root\\cimv2\\mdm\\dmmap' -ClassName 'MDM_EnterpriseModernAppManagement_AppManagement01' | Invoke-CimMethod -MethodName UpdateScanMethod";
        var (scan, _) = Run(powershell, $"-NoProfile -NonInteractive -Command \"{Scan}\"", TimeSpan.FromMinutes(5), null, null, cancel);
        details.Add(scan == 0 ? s["tune.store.scanOk"] : s.Format("tune.store.scanFailed", scan));

        var winget = Winget(s, "--source msstore", log, cancel);
        details.AddRange(winget.Details);
        var summary = winget.Installed is { } n ? s.Format("tune.store.summary", n) : s["tune.store.requested"];
        return new TuneupOutcome(TuneupTasks.StoreApps, scan == 0 || winget.Installed is not null, summary, details);
    }

    /// <summary>Installed programs with a newer version in winget's catalog.</summary>
    public static TuneupOutcome DesktopApps(Strings s, Action<string>? log = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        var winget = Winget(s, "--source winget", log, cancel);
        var summary = winget.Installed is { } n ? s.Format("tune.apps.summary", n) : s["tune.apps.noWinget"];
        return new TuneupOutcome(TuneupTasks.DesktopApps, winget.Installed is not null, summary, winget.Details);
    }

    private static (int? Installed, List<string> Details) Winget(Strings s, string source, Action<string>? log, CancellationToken cancel)
    {
        var details = new List<string>();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(exe))
        {
            details.Add(s["tune.apps.noWinget"]);
            return (null, details);
        }

        log?.Invoke(s["tune.apps.running"]);
        var (code, output) = Run(exe, $"upgrade --all {source} --accept-source-agreements --accept-package-agreements --silent --disable-interactivity",
            TimeSpan.FromHours(1), Encoding.UTF8, line =>
            {
                if (line.Contains("Instal", StringComparison.OrdinalIgnoreCase) || line.Contains("Found", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Encontrado", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke(line.Trim());
                }
            }, cancel);
        var installed = Tuneup.WingetInstalled(output);
        details.Add(Tuneup.WingetNothingToDo(output) && installed == 0 ? s["tune.apps.upToDate"] : s.Format("tune.apps.done", installed, code));
        return (installed, details);
    }

    // ---- Processes ----------------------------------------------------------------------------------------------------

    /// <summary>Runs a tool hidden, waits for it (or the timeout / cancellation) and returns its exit code and output.</summary>
    private static (int Code, string Output) Run(string file, string arguments, TimeSpan timeout, Encoding? encoding, Action<string>? line, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (encoding is not null)
        {
            start.StandardOutputEncoding = encoding;
            start.StandardErrorEncoding = encoding;
        }

        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } text)
            {
                lock (output)
                {
                    output.AppendLine(text);
                }

                line?.Invoke(text);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } text)
            {
                lock (output)
                {
                    output.AppendLine(text);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var clock = Stopwatch.StartNew();
        while (!process.WaitForExit(500))
        {
            if (cancel.IsCancellationRequested || clock.Elapsed > timeout)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // already gone
                }

                break;
            }
        }

        process.WaitForExit();
        lock (output)
        {
            return (process.HasExited ? process.ExitCode : -1, output.ToString());
        }
    }
}
