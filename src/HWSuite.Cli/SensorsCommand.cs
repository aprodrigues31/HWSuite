using System.Globalization;
using System.Text;
using HWSuite.Core;
using HWSuite.Core.Localization;
using HWSuite.Core.Sensors;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite sensors": one snapshot, a refreshing table (--watch) or a CSV log (--log).</summary>
internal static class SensorsCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        var access = AccessStatus.Detect();
        var isElevatedChild = options.ResultPath is not null;
        if (!isElevatedChild)
        {
            if (!access.PawnIoInstalled && OfferDriver(options, s))
            {
                access = AccessStatus.Detect();
            }

            if (!access.IsElevated && OfferElevation(options, s) is { } childExitCode)
            {
                return childExitCode;
            }
        }

        // The elevated copy started for a one-shot table writes to the file the parent shows.
        using var resultFile = isElevatedChild
            ? new StreamWriter(options.ResultPath!, append: false, new UTF8Encoding(false)) { AutoFlush = true }
            : null;
        var output = resultFile ?? Console.Out;

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // let the loop close hardware handles and flush the log
            stop.Cancel();
        };
        if (options.DurationSeconds is { } duration)
        {
            stop.CancelAfter(TimeSpan.FromSeconds(duration));
        }

        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        using var source = ProviderCatalog.CreateSensorSource(access);

        // The first update primes rate-based sensors (CPU load, throughput); show the second one.
        source.Read();
        if (Wait(TimeSpan.FromSeconds(1), stop.Token))
        {
            return 0;
        }

        if (options.LogPath is not null)
        {
            WriteAccessNotes(access, s, output);
            return Log(source, options.LogPath, interval, s, stop.Token);
        }

        if (options.Watch)
        {
            Watch(source, access, interval, s, stop.Token);
            return 0;
        }

        WriteTable(source.Read(), access, s, output);
        return 0;
    }

    /// <summary>Offers to install PawnIO when it is missing; returns true when an install was attempted.</summary>
    private static bool OfferDriver(CliOptions options, Strings s)
    {
        if (!options.AssumeYes)
        {
            if (Console.IsInputRedirected || options.LogPath is not null)
            {
                return false; // never block unattended runs on a question
            }

            if (DriverCommand.Confirm(s["sensors.offerDriver"]) != true)
            {
                return false;
            }
        }

        DriverCommand.Change(options with { DriverAction = DriverAction.Install, AssumeYes = true }, s, Console.Out, install: true);
        Console.WriteLine();
        return true;
    }

    /// <summary>
    /// Offers to reopen elevated. Returns the elevated run's exit code when it happened, null to
    /// continue here without elevation.
    /// </summary>
    private static int? OfferElevation(CliOptions options, Strings s)
    {
        if (!options.AssumeYes && (Console.IsInputRedirected || DriverCommand.Confirm(s["sensors.offerElevate"]) != true))
        {
            return null;
        }

        var keepsRunning = options.Watch || options.LogPath is not null;
        return Elevation.Run(ElevatedArguments(options), s, Console.Out, capture: !keepsRunning);
    }

    internal static string ElevatedArguments(CliOptions options)
    {
        var args = new List<string> { "sensors" };
        if (options.Watch)
        {
            args.Add("--watch");
        }

        if (options.LogPath is not null)
        {
            args.Add("--log");
            args.Add(Elevation.Quote(Path.GetFullPath(options.LogPath)));
        }

        args.Add("--interval");
        args.Add(options.IntervalSeconds.ToString(CultureInfo.InvariantCulture));
        if (options.DurationSeconds is { } duration)
        {
            args.Add("--duration");
            args.Add(duration.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(' ', args);
    }

    internal static void WriteAccessNotes(AccessStatus access, Strings s, TextWriter output)
    {
        if (!access.IsElevated)
        {
            output.WriteLine(s["sensors.notElevated"]);
        }

        if (!access.PawnIoInstalled)
        {
            output.WriteLine(s["sensors.noPawnIo"]);
        }
    }

    /// <summary>Access notes, plus the zero-readings hint when access looks fine but data does not.</summary>
    private static void WriteHints(SensorSnapshot snapshot, AccessStatus access, Strings s, TextWriter output)
    {
        WriteAccessNotes(access, s, output);
        if (access.IsElevated && access.PawnIoInstalled && SensorDiagnostics.CpuLowLevelReadsBlocked(snapshot))
        {
            output.WriteLine(s["sensors.lowLevelZero"]);
        }
    }

    private static void Watch(ISensorSource source, AccessStatus access, TimeSpan interval, Strings s, CancellationToken stop)
    {
        do
        {
            var snapshot = source.Read();
            var buffer = new StringWriter(s.Culture);
            buffer.WriteLine($"{s["sensors.title"]} · HWSuite {BuildInfo.Current.Display} · {snapshot.Timestamp.ToString("T", s.Culture)}");
            buffer.WriteLine(s.Format("sensors.watchHint", interval.TotalSeconds));
            WriteHints(snapshot, access, s, buffer);
            SensorTextWriter.Write(snapshot, s, buffer);

            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }

            Console.Write(buffer.ToString());
        }
        while (!Wait(interval, stop));
    }

    private static int Log(ISensorSource source, string logPath, TimeSpan interval, Strings s, CancellationToken stop)
    {
        var path = Path.GetFullPath(logPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var logger = new SensorCsvLogger(file);

        Console.WriteLine(s.Format("sensors.logging", path, interval.TotalSeconds));
        var samples = 0;
        do
        {
            logger.Append(source.Read());
            samples++;
            if (!Console.IsOutputRedirected)
            {
                Console.Write($"\r{samples}");
            }
        }
        while (!Wait(interval, stop));

        Console.WriteLine();
        Console.WriteLine(s.Format("sensors.logDone", samples, path));
        return 0;
    }

    private static void WriteTable(SensorSnapshot snapshot, AccessStatus access, Strings s, TextWriter output)
    {
        output.WriteLine($"{s["sensors.title"]} · HWSuite {BuildInfo.Current.Display} · {snapshot.Timestamp.ToString("g", s.Culture)}");
        WriteHints(snapshot, access, s, output);
        SensorTextWriter.Write(snapshot, s, output);
    }

    /// <summary>Sleeps for <paramref name="delay"/>; returns true when stop was requested.</summary>
    private static bool Wait(TimeSpan delay, CancellationToken stop) => stop.WaitHandle.WaitOne(delay);
}
