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
        var args = cli is null ? MaintenancePlan.AppArgument : MaintenancePlan.Arguments(schedule);
        Register(schedule, exe, args, description);
    }

    /// <summary>Name of the task in the root folder, used when the \HWSuite folder cannot be used.</summary>
    private const string RootName = "HWSuite Maintenance";

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

    /// <summary>
    /// Builds the task with the Task Scheduler object model (no XML): weekly on the chosen days, as the signed-in
    /// user with the highest rights (no password), starts late when missed, wakes the PC, 6 h limit. Goes into the
    /// \HWSuite folder; if Windows refuses that folder, into the root as "HWSuite Maintenance". The error names
    /// the step that failed.
    /// </summary>
    private static void Register(MaintenanceSchedule schedule, string exe, string args, string description)
    {
        var step = "connect";
        try
        {
            var service = Service();
            step = "definition";
            dynamic task = service.NewTask(0);
            task.RegistrationInfo.Author = "HWSuite";
            task.RegistrationInfo.Description = description;
            task.Principal.LogonType = InteractiveToken;
            task.Principal.RunLevel = 1; // highest
            task.Settings.StartWhenAvailable = true;
            task.Settings.WakeToRun = true;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT6H";
            task.Settings.MultipleInstances = 2; // ignore a new start while one runs
            step = "trigger";
            dynamic trigger = task.Triggers.Create(3); // weekly
            trigger.StartBoundary = (DateTime.Today + schedule.Time.ToTimeSpan()).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            trigger.DaysOfWeek = (short)schedule.Days.Distinct().Sum(d => 1 << (int)d); // Sunday = 1 … Saturday = 64
            trigger.WeeksInterval = (short)1;
            step = "action";
            dynamic action = task.Actions.Create(0); // run a program
            action.Path = exe;
            action.Arguments = args;
            action.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            step = "register";
            try
            {
                var folder = TaskFolder(service, create: true)!;
                folder.RegisterTaskDefinition("Maintenance", task, CreateOrUpdate, null, null, InteractiveToken, null);
                DeleteIn(service.GetFolder(@"\"), RootName);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                step = "register (root folder)";
                service.GetFolder(@"\").RegisterTaskDefinition(RootName, task, CreateOrUpdate, null, null, InteractiveToken, null);
            }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new InvalidOperationException($"{ex.Message} (0x{ex.HResult:X8}, {step})", ex);
        }
    }

    private static void DeleteIn(dynamic? folder, string name)
    {
        try
        {
            folder?.DeleteTask(name, 0);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // not there
        }
    }

    public static void Delete()
    {
        try
        {
            var service = Service();
            DeleteIn(TaskFolder(service, create: false), "Maintenance");
            DeleteIn(service.GetFolder(@"\"), RootName);
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
            var service = Service();
            foreach (var (folder, name) in new (dynamic?, string)[] { (TaskFolder(service, create: false), "Maintenance"), (service.GetFolder(@"\"), RootName) })
            {
                try
                {
                    if (folder is not null)
                    {
                        folder.GetTask(name);
                        return true;
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // try the next place
                }
            }

            return false;
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
