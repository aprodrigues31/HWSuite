using System.Globalization;
using HWSuite.Core.Localization;

namespace HWSuite.Core.Network;

/// <summary>Wi-Fi test lines shared by the app and the command line.</summary>
public static class WifiText
{
    /// <summary>Label / value pairs of the connection card.</summary>
    public static IReadOnlyList<(string Label, string Value)> Connection(WifiStatus w, Strings s)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        var rows = new List<(string, string)> { (s["wifi.adapter"], w.Adapter) };
        if (!w.Connected)
        {
            rows.Add((s["wifi.connection"], s["wifi.notConnected"]));
            return rows;
        }

        rows.Add((s["wifi.network"], w.Ssid ?? "—"));
        if (w.Bssid is { } bssid)
        {
            rows.Add((s["wifi.ap"], bssid));
        }

        if (w.Rssi is { } rssi)
        {
            rows.Add((s["wifi.signal"], $"{rssi} dBm · {w.SignalPercent}% · {s[WifiAnalysis.SignalKey(rssi)]}"));
        }

        if (w.BandGhz is { } band)
        {
            rows.Add((s["wifi.band"], $"{band.ToString("0.#", c)} GHz · {w.Channel?.ToString(c) ?? "—"}" + (WifiAnalysis.IsDfs(band, w.Channel ?? 0) ? " (DFS)" : "")));
        }

        if (w.WidthMhz is { } width)
        {
            rows.Add((s["wifi.width"], $"{width} MHz"));
        }

        if (w.Standard is { } standard)
        {
            rows.Add((s["wifi.standard"], standard));
        }

        if (w.RxMbps is not null || w.TxMbps is not null)
        {
            rows.Add((s["wifi.rate"], $"{Mbps(w.RxMbps, c)} / {Mbps(w.TxMbps, c)} Mbps"));
        }

        if (w.Security is { } security)
        {
            rows.Add((s["wifi.security"], w.Cipher is { } cipher ? $"{security} · {cipher}" : security));
        }

        if (w.Gateway is { } gateway)
        {
            rows.Add((s["wifi.gateway"], gateway));
        }

        if (w.DriverVersion is { } version)
        {
            var date = w.DriverDate?.ToString("d", c);
            rows.Add((s["wifi.driver"], string.Join(" · ", new[] { w.DriverProvider, version, date }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }

        return rows;
    }

    /// <summary>The stability lines: router, Internet, signal, roaming.</summary>
    public static IReadOnlyList<string> Stability(WifiStability st, Strings s)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        string Line(PingStats p) => s.Format("wifi.pingLine", Ms(p.AverageMs, c), Ms(p.JitterMs, c), p.LossPercent.ToString("0.#", c));
        var lines = new List<string>
        {
            $"{s["wifi.gw"]} ({st.Gateway.Target}): {Line(st.Gateway)}",
            $"{s["wifi.internet"]} ({st.Internet.Target}): {Line(st.Internet)}",
        };
        if (st.RssiMin is { } min && st.RssiMax is { } max)
        {
            lines.Add(s.Format("wifi.signalRange", min, max));
        }

        lines.Add(s.Format("wifi.roams", st.Roams) + " · " + s.Format("wifi.drops", st.Drops));
        return lines;
    }

    /// <summary>A tip's arguments with numbers written for the culture ("2,4" GHz in Portuguese).</summary>
    public static object[] Args(NetAdvice tip, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(tip);
        return [.. tip.Args.Select(a => a is double d ? d.ToString("0.#", culture) : a)];
    }

    private static string Mbps(double? value, CultureInfo c) => value is { } v ? v.ToString("0", c) : "—";

    private static string Ms(double? value, CultureInfo c) => value is { } v ? v.ToString("0.0", c) : "—";
}
