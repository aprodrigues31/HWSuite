using System.Diagnostics;
using System.Runtime.Versioning;
using HWSuite.Core.Maintenance;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Programs that start with Windows (Run keys and Startup folders) and their on/off switch. Switching uses the
/// same "StartupApproved" values as Task Manager, so nothing is deleted and both tools always agree.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsStartup
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupEntry> List()
    {
        var entries = new List<StartupEntry>();
        ReadRun(entries, Registry.CurrentUser, Run, StartupSource.UserRun);
        ReadRun(entries, Registry.LocalMachine, Run, StartupSource.MachineRun);
        ReadRun(entries, Registry.LocalMachine, Run32, StartupSource.MachineRun32);
        ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
        ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder);
        ReadTasks(entries);
        return StartupRules.Sort(entries);
    }

    /// <summary>Turns an entry on or off. Throws <see cref="UnauthorizedAccessException"/> for an all-users entry without admin rights.</summary>
    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Source == StartupSource.Task)
        {
            SetTaskEnabled(entry.Name, enabled);
            return;
        }

        var (hive, sub) = ApprovedKey(entry.Source);
        try
        {
            using var key = hive.CreateSubKey(Approved + sub, writable: true)
                ?? throw new UnauthorizedAccessException(Approved + sub);
            key.SetValue(entry.Name, StartupRules.Approved(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
        }
        catch (System.Security.SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }

    /// <summary>Scheduled tasks (outside \Microsoft) that run at sign-in or at boot, read from their XML files.</summary>
    private static void ReadTasks(List<StartupEntry> entries)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(root))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            var path = "\\" + Path.GetRelativePath(root, file);
            if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string xml;
            try
            {
                xml = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            var task = StartupRules.ParseTask(xml);
            if (!task.AtStartup || task.Command is null)
            {
                continue;
            }

            var command = string.IsNullOrEmpty(task.Arguments) ? task.Command : $"{task.Command} {task.Arguments}";
            var exe = StartupRules.Executable(Environment.ExpandEnvironmentVariables(task.Command.Contains(' ', StringComparison.Ordinal) && !task.Command.StartsWith('"') ? $"\"{task.Command}\"" : task.Command));
            var entry = Make(StartupSource.Task, path, command, exe) with { Enabled = task.Enabled };
            if (entry.Description is null)
            {
                entry = entry with { Description = Path.GetFileName(path) };
            }

            entries.Add(entry);
        }
    }

    private static void SetTaskEnabled(string path, bool enabled)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "/Change", "/TN", path, enabled ? "/ENABLE" : "/DISABLE" })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new IOException("schtasks");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        if (process.ExitCode != 0)
        {
            throw new UnauthorizedAccessException(string.IsNullOrWhiteSpace(error) ? $"schtasks {process.ExitCode}" : error.Trim());
        }
    }

    private static (RegistryKey Hive, string Sub) ApprovedKey(StartupSource source) => source switch
    {
        StartupSource.UserRun => (Registry.CurrentUser, "Run"),
        StartupSource.MachineRun => (Registry.LocalMachine, "Run"),
        StartupSource.MachineRun32 => (Registry.LocalMachine, "Run32"),
        StartupSource.UserFolder or StartupSource.Task => (Registry.CurrentUser, "StartupFolder"),
        _ => (Registry.LocalMachine, "StartupFolder"),
    };

    private static byte[]? ApprovedValue(StartupSource source, string name)
    {
        var (hive, sub) = ApprovedKey(source);
        try
        {
            using var key = hive.OpenSubKey(Approved + sub);
            return key?.GetValue(name) as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static void ReadRun(List<StartupEntry> entries, RegistryKey hive, string path, StartupSource source)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name) || key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                entries.Add(Make(source, name, command, StartupRules.Executable(Environment.ExpandEnvironmentVariables(command))));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // a key we cannot read: leave it out
        }
    }

    private static void ReadFolder(List<StartupEntry> entries, string folder, StartupSource source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) ?? file : file;
                entries.Add(Make(source, name, file, target));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // folder not readable
        }
    }

    private static StartupEntry Make(StartupSource source, string name, string command, string? exe)
    {
        string? publisher = null;
        string? description = null;
        var missing = false;
        if (exe is not null)
        {
            var path = exe;
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Environment.SystemDirectory, path);
            }

            if (File.Exists(path))
            {
                exe = path;
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
                    description = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
                }
                catch (FileNotFoundException)
                {
                    // gone between the two calls
                }
            }
            else
            {
                missing = Path.IsPathRooted(exe);
            }
        }

        return new StartupEntry(source, name, command, exe, publisher, description, StartupRules.IsEnabled(ApprovedValue(source, name)), missing);
    }

    /// <summary>The target of a .lnk file, read from its Shell Link header (no COM): the local base path, if any.</summary>
    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var data = File.ReadAllBytes(lnk);
            if (data.Length < 0x4C || BitConverter.ToInt32(data, 0) != 0x4C)
            {
                return null;
            }

            var flags = BitConverter.ToInt32(data, 0x14);
            var pos = 0x4C;
            if ((flags & 0x01) != 0)
            {
                pos += 2 + BitConverter.ToUInt16(data, pos); // LinkTargetIDList
            }

            if ((flags & 0x02) == 0 || pos + 0x1C > data.Length)
            {
                return null; // no LinkInfo
            }

            var info = pos;
            var basePath = BitConverter.ToInt32(data, info + 0x10);
            if (basePath <= 0 || info + basePath >= data.Length)
            {
                return null;
            }

            var start = info + basePath;
            var end = Array.IndexOf(data, (byte)0, start);
            return end > start ? System.Text.Encoding.Default.GetString(data, start, end - start) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
