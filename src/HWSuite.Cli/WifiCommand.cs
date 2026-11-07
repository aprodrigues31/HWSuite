using System.Globalization;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Network;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite wifi [--duration 30]": connection, channel health, stability to the router and the Internet, tips.</summary>
internal static class WifiCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var c = s.Culture;
        Console.WriteLine(s["wifi.scanning"]);
        var status = WlanReader.Status(scan: true, cancel.Token);
        if (status is null)
        {
            Console.Error.WriteLine(s["wifi.none"]);
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"== {s["wifi.connection"]} ==");
        foreach (var (label, value) in WifiText.Connection(status, s))
        {
            Console.WriteLine($"  {label,-30} {value}");
        }

        if (status.ScanBlocked)
        {
            Console.WriteLine();
            Console.WriteLine("  " + s["wifi.blocked"]);
        }

        foreach (var band in new[] { 2.4, 5, 6 }.Where(b => status.Networks.Any(n => n.BandGhz == b)))
        {
            var loads = WifiAnalysis.Channels(status.Networks, band, status.Ssid);
            Console.WriteLine();
            Console.WriteLine($"== {s["wifi.channels"]} · {band.ToString("0.#", c)} GHz · {s.Format("wifi.channels.best", WifiAnalysis.Best(loads)?.Channel ?? 0)} ==");
            foreach (var load in loads.Where(l => l.Networks > 0 || l.Channel == status.Channel || band == 2.4))
            {
                var bar = new string('#', (int)Math.Min(30, Math.Round(load.Score * 3)));
                var mine = load.Channel == status.Channel && status.BandGhz == band ? " <- " + s["wifi.channels.yours"] : "";
                Console.WriteLine($"  ch {load.Channel,3}{(load.Dfs ? "*" : " ")} {bar,-30} {s.Format("wifi.channels.networks", load.Networks)}{mine}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"== {s.Format("wifi.networks", status.Networks.Count)} ==");
        foreach (var n in status.Networks.Take(25))
        {
            Console.WriteLine($"  {(n.Connected ? "*" : " ")} {Trim(n.Ssid ?? s["wifi.networks.hidden"], 28),-28} {n.BandGhz?.ToString("0.#", c),-4} ch {n.Channel,-4} {n.WidthMhz,3} MHz {n.Rssi,4} dBm  {n.Standard}");
        }

        WifiStability? stability = null;
        if (status.Connected)
        {
            var seconds = options.DurationSeconds ?? 30;
            if (status.Gateway is { } gateway)
            {
                Console.WriteLine();
                Console.WriteLine($"== {s["wifi.stability"]} ({seconds.ToString("0", c)} s) ==");
                var progress = new Progress<WifiSample>(x => Console.Write($"\r  {s.Format("wifi.running", x.Seconds.ToString("0", c), seconds.ToString("0", c))}   "));
                stability = WifiAnalysis.RunStabilityAsync(() => WlanReader.Link(status.InterfaceId), gateway, "1.1.1.1", TimeSpan.FromSeconds(seconds), progress, cancel.Token)
                    .GetAwaiter().GetResult();
                Console.WriteLine();
                foreach (var line in WifiText.Stability(stability, s))
                {
                    Console.WriteLine("  " + line);
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("  " + s["wifi.noGateway"]);
            }
        }

        var advice = WifiAnalysis.Advice(status, stability, DateTime.Today);
        Console.WriteLine();
        Console.WriteLine($"== {s["wifi.tips"]} ==");
        foreach (var tip in advice)
        {
            var args = WifiText.Args(tip, c);
            var mark = tip.Level switch { AdviceLevel.Problem => "!", AdviceLevel.Good => "+", _ => "-" };
            Console.WriteLine($"  [{mark}] {s.Format(tip.Key + ".title", args)}");
            Console.WriteLine($"      {s.Format(tip.Key + ".body", args)}");
        }

        try
        {
            WifiSummary.Save(BenchmarkFiles.Wifi, WifiSummary.From(status, stability, advice, DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not kept
        }

        return advice.Any(a => a.Level == AdviceLevel.Problem) ? 1 : 0;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
