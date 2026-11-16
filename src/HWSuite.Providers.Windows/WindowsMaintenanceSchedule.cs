using System.Runtime.Versioning;
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
        Register(xml);
    }

    // Task Scheduler through its COM API (Schedule.Service): the \HWSuite folder is created when missing and errors
    // come back as exceptions with proper text. (schtasks.exe /XML failed on some PCs with "path not found".)
    private const int CreateOrUpdate = 6;
    private const int InteractiveToken = 3;

    private static dynamic Service()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler (Schedule.Service)");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler (Schedule.Service)");
        service.Connect();
        return service;
    }

    private static dynamic? TaskFolder(dynamic service, bool create)
    {
        try
        {
            return service.GetFolder(@"\HWSuite");
        }
        catch (System.Runtime.InteropServices.COMException) when (create)
        {
            return service.GetFolder(@"\").CreateFolder("HWSuite");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static void Register(string xml)
    {
        try
        {
            var service = Service();
            var folder = TaskFolder(service, create: true)!;
            folder.RegisterTask("Maintenance", xml, CreateOrUpdate, null, null, InteractiveToken, null);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new InvalidOperationException($"{ex.Message} (0x{ex.HResult:X8})", ex);
        }
    }

    public static void Delete()
    {
        try
        {
            var folder = TaskFolder(Service(), create: false);
            folder?.DeleteTask("Maintenance", 0);
        }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070002)
        {
            // not there: nothing to delete
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new InvalidOperationException($"{ex.Message} (0x{ex.HResult:X8})", ex);
        }
    }

    public static bool Exists()
    {
        try
        {
            var folder = TaskFolder(Service(), create: false);
            if (folder is null)
            {
                return false;
            }

            folder.GetTask("Maintenance");
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
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
