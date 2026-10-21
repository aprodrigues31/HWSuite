using System.Diagnostics;
using System.Security;
using System.Text;

namespace HWSuite.Providers.Windows;

/// <summary>
/// "Start with Windows" through Task Scheduler instead of the Run key: a logon task can start the
/// app with highest privileges, so sensors work without a UAC prompt at every sign-in. Creating
/// the task needs administrator rights; the task runs only for the user who created it.
/// </summary>
public static class StartupTask
{
    public const string TaskName = "HWSuite";

    /// <summary>Argument the task passes so the app starts hidden in the notification area.</summary>
    public const string MinimizedArgument = "--minimized";

    public static bool IsRegistered(string taskName = TaskName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var (code, _) = Schtasks($"/Query /TN \"{taskName}\"");
        return code == 0;
    }

    /// <summary>Creates or replaces the logon task. Returns null on success, the error text otherwise.</summary>
    public static string? Register(string executablePath, string taskName = TaskName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var file = Path.Combine(Path.GetTempPath(), $"hwsuite-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks expects the XML in UTF-16, which is also what the declaration says.
            File.WriteAllText(file, BuildTaskXml(executablePath, user), Encoding.Unicode);
            var (code, output) = Schtasks($"/Create /TN \"{taskName}\" /XML \"{file}\" /F");
            return code == 0 ? null : output;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Removes the task; returns null on success or when it did not exist.</summary>
    public static string? Unregister(string taskName = TaskName)
    {
        if (!IsRegistered(taskName))
        {
            return null;
        }

        var (code, output) = Schtasks($"/Delete /TN \"{taskName}\" /F");
        return code == 0 ? null : output;
    }

    /// <summary>Task definition: at the user's logon, after 15 s, run the app with highest privileges.</summary>
    public static string BuildTaskXml(string executablePath, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var exe = SecurityElement.Escape(executablePath);
        var user = SecurityElement.Escape(userId);
        var folder = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>HWSuite: hardware monitoring in the notification area.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT15S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>{MinimizedArgument}</Arguments>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) Schtasks(string arguments)
    {
        var path = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        using var process = Process.Start(new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("schtasks.exe did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "schtasks.exe timed out");
        }

        return (process.ExitCode, (stderr.Trim() + " " + stdout.Result.Trim()).Trim());
    }
}
