using System.Globalization;
using HWSuite.Core.Localization;
using HWSuite.Core.Snmp;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite snmp HOST [--community C | --user U --auth SHA --auth-pass P --priv AES --priv-pass P] [--port 161]
/// [--v1|--v2c] [--walk OID]": system, ports and traffic of a network device, or a raw walk.
/// </summary>
internal static class SnmpCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        var args = options.SnmpArgs;
        if (args.Count == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("hwsuite snmp HOST [--community C | --user U --auth SHA --auth-pass P --priv AES --priv-pass P] [--port 161] [--v1|--v2c] [--walk OID]");
            return 2;
        }

        string? Arg(string name)
        {
            var i = args.ToList().FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        bool Flag(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        var community = Arg("--community");
        var version = Flag("--v1") ? SnmpVersion.V1 : Flag("--v2c") || (community is not null && Arg("--user") is null) ? SnmpVersion.V2c : SnmpVersion.V3;
        var auth = (Arg("--auth") ?? "SHA").ToUpperInvariant().Replace("-", "", StringComparison.Ordinal) switch
        {
            "MD5" => SnmpAuth.Md5,
            "SHA224" => SnmpAuth.Sha224,
            "SHA256" => SnmpAuth.Sha256,
            "SHA384" => SnmpAuth.Sha384,
            "SHA512" => SnmpAuth.Sha512,
            "NONE" => SnmpAuth.None,
            _ => SnmpAuth.Sha1,
        };
        var priv = (Arg("--priv") ?? (Arg("--priv-pass") is null ? "NONE" : "AES")).ToUpperInvariant().Replace("-", "", StringComparison.Ordinal) switch
        {
            "DES" => SnmpPriv.Des,
            "AES192" => SnmpPriv.Aes192,
            "AES256" => SnmpPriv.Aes256,
            "NONE" => SnmpPriv.None,
            _ => SnmpPriv.Aes128,
        };
        if (Arg("--auth-pass") is null)
        {
            auth = SnmpAuth.None;
            priv = SnmpPriv.None;
        }

        var port = int.TryParse(Arg("--port"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 161;
        var target = new SnmpTarget(args[0], port, version, community, Arg("--user"), auth, Arg("--auth-pass"), priv, Arg("--priv-pass") ?? Arg("--auth-pass"));
        using var client = new SnmpClient(target);
        var c = s.Culture;
        try
        {
            if (Arg("--walk") is { } root)
            {
                foreach (var vb in client.WalkAsync(root).GetAwaiter().GetResult())
                {
                    Console.WriteLine($"{vb.Oid} = {vb.Value}");
                }

                return 0;
            }

            var first = SnmpReader.PollAsync(client).GetAwaiter().GetResult();
            Thread.Sleep(3000);
            var second = SnmpReader.PollAsync(client).GetAwaiter().GetResult();
            var sys = second.System;
            Console.WriteLine($"{s["snmp.name"]}: {sys.Name}");
            Console.WriteLine($"{s["snmp.description"]}: {sys.Description}");
            if (sys.Uptime is { } up)
            {
                Console.WriteLine($"{s["snmp.uptime"]}: {(int)up.TotalDays} d {up.Hours} h {up.Minutes} min");
            }

            if (sys.CpuPercent is { } cpu)
            {
                Console.WriteLine($"{s["snmp.cpu"]}: {cpu.ToString("0", c)}%");
            }

            if (sys.MemoryUsedPercent is { } mem)
            {
                Console.WriteLine($"{s["snmp.memory"]}: {mem.ToString("0", c)}%");
            }

            var rates = SnmpReader.Rates(first, second).ToDictionary(r => r.Index);
            Console.WriteLine();
            Console.WriteLine($"  {s["snmp.port"],-28} {s["snmp.speed"],-10} {"↓ Mbps",9} {"↑ Mbps",9} {s["snmp.errors"],8}");
            foreach (var port1 in second.Ports.Where(x => !x.IsSoftware))
            {
                rates.TryGetValue(port1.Index, out var r);
                var state = port1.OperUp ? "" : $" ({s["snmp.down"]})";
                var label = (port1.Label + state).Length > 28 ? (port1.Label + state)[..27] + "…" : port1.Label + state;
                Console.WriteLine($"  {label,-28} {(port1.OperUp && port1.SpeedMbps is { } sp ? SnmpReader.Speed(sp) : "—"),-10} {(r?.InMbps ?? 0).ToString("0.00", c),9} {(r?.OutMbps ?? 0).ToString("0.00", c),9} {((port1.InErrors ?? 0) + (port1.OutErrors ?? 0)).ToString("N0", c),8}");
            }

            return 0;
        }
        catch (SnmpException ex)
        {
            Console.Error.WriteLine($"SNMP: {ex.Error} - {ex.Message}");
            return 1;
        }
    }
}
