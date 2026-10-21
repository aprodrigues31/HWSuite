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
    private const int ExitCancelled = Elevation.ExitCancelled;

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
            var code = Elevation.Run(install ? "driver install --yes" : "driver uninstall --yes", s, output, capture: true);
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

    /// <summary>
    /// Asks a yes/no question; null when there is no interactive console to ask on. The prompt goes
    /// to the error stream so it stays visible when the output is redirected to a file.
    /// </summary>
    internal static bool? Confirm(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        Console.Error.Write(prompt);
        Console.Error.Flush();
        var answer = Console.ReadLine();
        if (Console.IsOutputRedirected)
        {
            Console.Error.WriteLine();
        }

        return IsYes(answer);
    }

    internal static bool IsYes(string? answer) =>
        answer?.Trim().ToLowerInvariant() is "s" or "sim" or "y" or "yes";
}
