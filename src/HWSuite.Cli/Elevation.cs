using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using HWSuite.Core.Localization;

namespace HWSuite.Cli;

/// <summary>
/// Starts an elevated copy of hwsuite through UAC. In "capture" mode the copy writes its output
/// to a temporary file (passed with --result) that is shown here when it exits; otherwise the copy
/// runs in its own console window (for --watch and --log, which keep running).
/// </summary>
internal static class Elevation
{
    public const int ExitCancelled = 4;
    private const int ErrorCancelledByUser = 1223; // ERROR_CANCELLED: UAC prompt dismissed

    /// <summary>Runs <paramref name="arguments"/> elevated; returns the child's exit code, or 4 if UAC was declined.</summary>
    public static int Run(string arguments, Strings s, TextWriter output, bool capture)
    {
        output.WriteLine(s["driver.elevating"]);
        var resultPath = capture ? Path.Combine(Path.GetTempPath(), $"hwsuite-{Guid.NewGuid():N}.txt") : null;
        var language = s.Culture.Name.Length > 0 ? s.Culture.Name : CultureInfo.CurrentUICulture.Name;
        var full = $"{arguments} --lang {language}" + (resultPath is null ? "" : $" --result {Quote(resultPath)}");
        var (fileName, fullArguments) = SelfInvocation(full);

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

            if (resultPath is null)
            {
                output.WriteLine(s["sensors.elevatedWindow"]);
                return 0;
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
            if (resultPath is not null)
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

    internal static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
