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
        if (!access.PawnIoInstalled && OfferDriver(options, s))
        {
            access = AccessStatus.Detect();
        }

        WriteAccessNotes(access, s, Console.Error);

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
            return Log(source, options.LogPath, interval, s, stop.Token);
        }

        if (options.Watch)
        {
            Watch(source, interval, s, stop.Token);
            return 0;
        }

        WriteTable(source.Read(), s, Console.Out);
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

    private static void Watch(ISensorSource source, TimeSpan interval, Strings s, CancellationToken stop)
    {
        do
        {
            var snapshot = source.Read();
            var buffer = new StringWriter(s.Culture);
            buffer.WriteLine($"{s["sensors.title"]} · HWSuite {BuildInfo.Current.Display} · {snapshot.Timestamp.ToString("T", s.Culture)}");
            buffer.WriteLine(s.Format("sensors.watchHint", interval.TotalSeconds));
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

    private static void WriteTable(SensorSnapshot snapshot, Strings s, TextWriter output)
    {
        output.WriteLine($"{s["sensors.title"]} · HWSuite {BuildInfo.Current.Display} · {snapshot.Timestamp.ToString("g", s.Culture)}");
        SensorTextWriter.Write(snapshot, s, output);
    }

    /// <summary>Sleeps for <paramref name="delay"/>; returns true when stop was requested.</summary>
    private static bool Wait(TimeSpan delay, CancellationToken stop) => stop.WaitHandle.WaitOne(delay);
}
