using System.Net.Http;
using System.Net.WebSockets;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Network;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite net [--ipv6] [--mlab --accept-mlab-policy]": the Internet / Wi-Fi test.</summary>
internal static class NetCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        if (options.Mlab && !options.AcceptMlabPolicy)
        {
            Console.Error.WriteLine(s.Format("netcli.needConsent", MlabProbe.PrivacyPolicy));
            return 2;
        }

        var server = options.Mlab ? NetServer.MLab : NetServer.Cloudflare;
        var family = options.Ipv6 ? IpFamily.IPv6 : IpFamily.IPv4;
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var connection = ActiveConnection.Find(family, OperatingSystem.IsWindows() ? WlanReader.Query : null);
        Console.WriteLine(s.Format("netcli.title", NetworkTest.Version, server == NetServer.MLab ? "M-Lab" : "Cloudflare", family));
        var last = "";
        var progress = new Inline<NetProgress>(p =>
        {
            if (p.Phase != last && p.Phase != "done")
            {
                last = p.Phase;
                Console.WriteLine("  " + p.Phase + "...");
            }
        });

        NetworkTestResult result;
        try
        {
            using INetworkProbe probe = server == NetServer.MLab ? new MlabProbe(family) : new CloudflareProbe(family);
            result = NetworkTest.RunAsync(probe, server, family, connection, null, progress, cancel.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(s["bench.cancelled"]);
            return 130;
        }
        catch (NetworkTestException ex)
        {
            Console.Error.WriteLine(s[ex.Key] + (ex.InnerException is { } inner ? $" ({inner.Message})" : ""));
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException)
        {
            Console.Error.WriteLine($"{s["net.error.transfer"]} ({ex.Message})");
            return 1;
        }

        var c = s.Culture;
        string N(double v) => v.ToString(v >= 10 ? "N0" : "0.#", c);
        void Row(string label, string value) => Console.WriteLine(s.Format("netcli.row", label, value));
        Console.WriteLine();
        Row(s["net.download"], $"{N(result.DownloadMbps)} Mbps");
        Row(s["net.upload"], $"{N(result.UploadMbps)} Mbps");
        Row($"{s["net.latency"]} {s["net.idle"]}", $"{N(result.Idle.Summary.Median)} ms ({s["net.withDownload"]} {N(result.LoadedDownload.Summary.Median)}, {s["net.withUpload"]} {N(result.LoadedUpload.Summary.Median)})");
        Row($"{s["net.jitter"]} {s["net.idle"]}", $"{N(result.Idle.Jitter)} ms ({s["net.withDownload"]} {N(result.LoadedDownload.Jitter)}, {s["net.withUpload"]} {N(result.LoadedUpload.Jitter)})");
        Row($"{s["net.loss"]} {s["net.idle"]}", result.LossPercent is { } loss ? $"{loss.ToString("0.#", c)}%" : s["net.lossNa"]);
        Row(s["net.serverName"], result.Endpoint.Server ?? result.Endpoint.Host);
        if (result.Endpoint.Provider is { } provider)
        {
            Row(s["net.provider"], provider);
        }

        if (result.Connection is { } conn)
        {
            Row(s["net.connection"], $"{conn.Summary} · {conn.Description}");
        }

        Console.WriteLine();
        Console.WriteLine(s["netcli.tips"]);
        foreach (var tip in NetworkAdvice.For(result))
        {
            var args = tip.Args.Select(a => a is double d ? N(d) : a).ToArray();
            Console.WriteLine("  - " + s.Format(tip.Key + ".title", args));
        }

        try
        {
            NetworkHistory.Append(BenchmarkFiles.Network, result);
            Console.WriteLine();
            Console.WriteLine(BenchmarkFiles.Network);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
        }

        return 0;
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
