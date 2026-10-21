using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HWSuite.Core.Localization;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite driver status|install|uninstall". Changes always ask first (unless --yes) and run in
/// an elevated copy of hwsuite started through UAC; that copy writes its output to a temporary
/// file which this process shows when it finishes.
/// </summary>
internal static class DriverCommand
{
    private const int ExitCancelled = 4;
    private const int ErrorCancelledByUser = 1223; // ERROR_CANCELLED: UAC prompt dismissed

    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        if (options.ResultPath is null)
        {
            return Dispatch(options, s, Console.Out);
        }

        // Elevated child: everything goes to the result file the parent will print.
        using var result = new StreamWriter(options.ResultPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        return Dispatch(options, s, result);
    }

    private static int Dispatch(CliOptions options, Strings s, TextWriter output) => options.DriverAction switch
    {
        DriverAction.Install => Change(options, s, output, install: true),
        DriverAction.Uninstall => Change(options, s, output, install: false),
        _ => Status(s, output),
    };

    private static int Status(Strings s, TextWriter output)
    {
        var access = AccessStatus.Detect();
        output.WriteLine(s[access switch
        {
            { PawnIoRemovalPending: true } => "driver.statusPendingRemoval",
            { PawnIoInstalled: true } => "driver.statusInstalled",
            _ => "driver.statusMissing",
        }]);
        output.WriteLine(s[access.IsElevated ? "driver.statusElevated" : "driver.statusNotElevated"]);
        output.WriteLine();
        output.WriteLine(s.Format("driver.about", PawnIoInstaller.Homepage));
        return access.PawnIoInstalled ? 0 : 1;
    }

    /// <summary>Installs or removes the driver; returns 0 on success, 4 when the user declined.</summary>
    internal static int Change(CliOptions options, Strings s, TextWriter output, bool install)
    {
        var access = AccessStatus.Detect();
        if (install && access.PawnIoInstalled)
        {
            output.WriteLine(s.Format("driver.outcome.AlreadyInstalled"));
            return 0;
        }

        if (!install && (!access.PawnIoInstalled || access.PawnIoRemovalPending))
        {
            output.WriteLine(s.Format("driver.outcome.NotInstalled"));
            return 0;
        }

        var isElevatedChild = options.ResultPath is not null;
        if (!isElevatedChild && !options.AssumeYes)
        {
            output.WriteLine(s.Format("driver.about", PawnIoInstaller.Homepage));
            switch (Confirm(s[install ? "driver.confirmInstall" : "driver.confirmUninstall"]))
            {
                case null:
                    output.WriteLine(s["driver.needYes"]);
                    return 2;
                case false:
                    output.WriteLine(s["driver.cancelled"]);
                    return ExitCancelled;
            }
        }

        if (!access.IsElevated)
        {
            var code = RunElevated(options, s, output, install);
            if (code == 0 && install)
            {
                output.WriteLine(s["driver.nextStep"]);
            }

            return code;
        }

        void Progress(DriverStep step, string? detail) => output.WriteLine(s.Format($"driver.step.{step}", detail ?? "—"));

        var result = install ? PawnIoInstaller.Install(Progress) : PawnIoInstaller.Uninstall(Progress);
        output.WriteLine(s.Format($"driver.outcome.{result.Outcome}", result.Detail ?? ""));
        if (result.Outcome == DriverOutcome.Uninstalled && AccessStatus.Detect().PawnIoInstalled)
        {
            output.WriteLine(s["driver.rebootToFinish"]);
        }
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>Asks a yes/no question; null when there is no interactive console to ask on.</summary>
    internal static bool? Confirm(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        Console.Write(prompt);
        return IsYes(Console.ReadLine());
    }

    internal static bool IsYes(string? answer) =>
        answer?.Trim().ToLowerInvariant() is "s" or "sim" or "y" or "yes";

    private static int RunElevated(CliOptions options, Strings s, TextWriter output, bool install)
    {
        output.WriteLine(s["driver.elevating"]);
        var resultPath = Path.Combine(Path.GetTempPath(), $"hwsuite-driver-{Guid.NewGuid():N}.txt");
        var arguments = string.Join(' ',
            "driver",
            install ? "install" : "uninstall",
            "--yes",
            "--result", Quote(resultPath),
            "--lang", s.Culture.Name.Length > 0 ? s.Culture.Name : CultureInfo.CurrentUICulture.Name);

        var (fileName, fullArguments) = SelfInvocation(arguments);
        try
        {
            using var child = Process.Start(new ProcessStartInfo(fileName, fullArguments)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (child is null)
            {
                return 1;
            }

            child.WaitForExit();
            if (File.Exists(resultPath))
            {
                output.Write(File.ReadAllText(resultPath));
            }

            return child.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            output.WriteLine(s["driver.uacDenied"]);
            return ExitCancelled;
        }
        finally
        {
            try
            {
                File.Delete(resultPath);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>How to start this same program again: the published exe, or "dotnet hwsuite.dll" in development.</summary>
    internal static (string FileName, string Arguments) SelfInvocation(string arguments)
    {
        var processPath = Environment.ProcessPath ?? "hwsuite.exe";
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Development only ("dotnet hwsuite.dll"); the published single-file exe takes the branch below.
            var dll = Path.Combine(AppContext.BaseDirectory, "hwsuite.dll");
            return (processPath, $"{Quote(dll)} {arguments}");
        }

        return (processPath, arguments);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
