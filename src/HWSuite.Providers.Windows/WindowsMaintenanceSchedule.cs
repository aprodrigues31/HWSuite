using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Settings;

namespace HWSuite.Providers.Windows;

/// <summary>
/// The scheduled maintenance (a task in Windows' Task Scheduler that runs "hwsuite tuneup ... --scheduled"), the
/// history of runs and the durations measured on this computer, kept in HWSuite's data folder.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsMaintenanceSchedule
{
    public const string TaskName = @"\HWSuite\Maintenance";

    private static string Folder => DataLocation.Folder;

    public static string HistoryPath => Path.Combine(Folder, "maintenance-history.txt");

    private static string TimesPath => Path.Combine(Folder, "maintenance-times.txt");

    /// <summary>The command-line program next to the app (hwsuite.exe), or null when this copy does not have it.</summary>
    public static string? CliPath()
    {
        var path = Path.Combine(DataLocation.ExeFolder, "hwsuite.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Creates or replaces the scheduled task. Throws <see cref="InvalidOperationException"/> with the reason.</summary>
    /// <remarks>
    /// Runs hwsuite.exe when it is next to the app (installed copy); a portable copy that is just the app's .exe
    /// schedules the app itself with "--run-maintenance" (it runs the tasks without opening a window).
    /// </remarks>
    public static void Save(MaintenanceSchedule schedule, string description)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var cli = CliPath();
        var exe = cli ?? Environment.ProcessPath ?? throw new InvalidOperationException("HWSuite.Gui.exe");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var xml = MaintenancePlan.TaskXml(schedule, exe, user, DateTime.Today, description, cli is null ? MaintenancePlan.AppArgument : null);
        var file = Path.Combine(Path.GetTempPath(), $"hwsuite-task-{Environment.ProcessId}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            Schtasks("/Create", "/TN", TaskName, "/XML", file, "/F");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Delete()
    {
        if (Exists())
        {
            Schtasks("/Delete", "/TN", TaskName, "/F");
        }
    }

    public static bool Exists()
    {
        try
        {
            return Schtasks(false, "/Query", "/TN", TaskName) == 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Schtasks(params string[] args) => Schtasks(true, args);

    private static int Schtasks(bool throwOnError, params string[] args)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("schtasks");
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(20000);
        if (process.ExitCode != 0 && throwOnError)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"schtasks {process.ExitCode}" : error.Trim());
        }

        return process.ExitCode;
    }

    // ---- History and durations ---------------------------------------------------------------------------------

    public static IReadOnlyList<MaintenanceRun> History()
    {
        try
        {
            return File.Exists(HistoryPath) ? MaintenancePlan.ParseHistory(File.ReadAllText(HistoryPath)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Adds a run to the history (the last 50 are kept).</summary>
    public static void Record(MaintenanceRun run)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var runs = History().Take(49).Reverse().Append(run);
            File.WriteAllText(HistoryPath, string.Concat(runs.Select(MaintenancePlan.FormatRun)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // history is a convenience
        }
    }

    public static Dictionary<TuneupTasks, double> Times()
    {
        try
        {
            return File.Exists(TimesPath) ? MaintenancePlan.ParseTimes(File.ReadAllText(TimesPath)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void LearnTime(TuneupTasks task, double seconds)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var old = File.Exists(TimesPath) ? File.ReadAllText(TimesPath) : null;
            File.WriteAllText(TimesPath, MaintenancePlan.LearnTime(old, task, seconds));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // estimates stay at the defaults
        }
    }
}
