using System.Globalization;

namespace HWSuite.Core.Snmp;

/// <summary>
/// A device kept in Settings. The community and the passwords are stored protected (<c>Protected</c> fields hold
/// what the app's protector produced: on Windows DPAPI, tied to this Windows user and computer).
/// </summary>
public sealed record SnmpDeviceConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "";

    public string Host { get; init; } = "";

    public int Port { get; init; } = 161;

    public SnmpVersion Version { get; init; } = SnmpVersion.V3;

    public string? User { get; init; }

    public SnmpAuth Auth { get; init; } = SnmpAuth.Sha1;

    public SnmpPriv Priv { get; init; } = SnmpPriv.Aes128;

    public string? CommunityProtected { get; init; }

    public string? AuthPasswordProtected { get; init; }

    public string? PrivPasswordProtected { get; init; }

    /// <summary>Seconds between readings.</summary>
    public int PollSeconds { get; init; } = 10;

    /// <summary>The target with the secrets opened by <paramref name="unprotect"/>.</summary>
    public SnmpTarget Target(Func<string?, string?> unprotect)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        return new SnmpTarget(Host, Port, Version, unprotect(CommunityProtected), User, Auth, unprotect(AuthPasswordProtected), Priv,
            unprotect(PrivPasswordProtected));
    }
}

/// <summary>What the system group and HOST-RESOURCES tell about the device.</summary>
public sealed record SnmpSystem(string? Description, string? Name, string? ObjectId, TimeSpan? Uptime, string? Contact, string? Location, double? CpuPercent, double? MemoryUsedPercent);

/// <summary>One interface (ifTable + ifXTable).</summary>
public sealed record SnmpPort(
    int Index,
    string Name,
    string? Alias,
    string? Description,
    int Type,
    bool AdminUp,
    bool OperUp,
    double? SpeedMbps,
    ulong? InOctets,
    ulong? OutOctets,
    bool HighCapacity,
    ulong? InErrors,
    ulong? OutErrors,
    ulong? InDiscards,
    ulong? OutDiscards)
{
    /// <summary>Loopback and similar software interfaces nobody wants to watch.</summary>
    public bool IsSoftware => Type is 24 or 1 or 131 or 53;

    /// <summary>"WAN1", "WAN 2", "wan" in the name, alias or description: the router's Internet links.</summary>
    public bool IsWan => new[] { Name, Alias, Description }.Any(t => t is not null && t.Contains("WAN", StringComparison.OrdinalIgnoreCase));

    public string Label => string.IsNullOrWhiteSpace(Alias) || Alias == Name ? Name : $"{Name} · {Alias}";
}

/// <summary>One reading of a device.</summary>
public sealed record SnmpPoll(DateTimeOffset At, SnmpSystem System, IReadOnlyList<SnmpPort> Ports);

/// <summary>Traffic of a port between two readings.</summary>
public sealed record PortRate(int Index, double InMbps, double OutMbps, ulong NewErrors);

public enum SnmpEventKind
{
    DeviceDown,
    DeviceUp,
    PortDown,
    PortUp,
    SpeedChanged,
    Errors,
    Restarted,
}

/// <summary>Something worth telling: a device stopped answering, a port went down, a cable renegotiated at 100 Mbps...</summary>
public sealed record SnmpEvent(DateTimeOffset At, string DeviceId, SnmpEventKind Kind, string Subject, string? Detail, bool Problem);

/// <summary>Reads devices: system group, interfaces, processor and memory.</summary>
public static class SnmpReader
{
    private const string Sys = "1.3.6.1.2.1.1";
    private const string IfEntry = "1.3.6.1.2.1.2.2.1";
    private const string IfX = "1.3.6.1.2.1.31.1.1.1";
    private const string HrProcessorLoad = "1.3.6.1.2.1.25.3.3.1.2";
    private const string HrStorage = "1.3.6.1.2.1.25.2.3.1";
    private const string HrStorageRam = "1.3.6.1.2.1.25.2.1.2";

    public static async Task<SnmpPoll> PollAsync(SnmpClient client, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var system = await client.GetAsync(
            [$"{Sys}.1.0", $"{Sys}.5.0", $"{Sys}.2.0", $"{Sys}.3.0", $"{Sys}.4.0", $"{Sys}.6.0"], cancel).ConfigureAwait(false);
        string? Text(int i) => i < system.Count && !system[i].Value.IsException && system[i].Value.Tag != SnmpTag.Null ? system[i].Value.ToString() : null;
        TimeSpan? uptime = system.Count > 3 && system[3].Value.AsLong() is { } ticks ? TimeSpan.FromMilliseconds(ticks * 10) : null;

        async Task<Dictionary<int, SnmpValue>> Column(string root, int column)
        {
            var map = new Dictionary<int, SnmpValue>();
            try
            {
                foreach (var vb in await client.WalkAsync($"{root}.{column}", cancel: cancel).ConfigureAwait(false))
                {
                    var last = vb.Oid[(vb.Oid.LastIndexOf('.') + 1)..];
                    if (int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    {
                        map[index] = vb.Value;
                    }
                }
            }
            catch (SnmpException ex) when (ex.Error == SnmpError.AgentError)
            {
                // column not supported
            }

            return map;
        }

        var descr = await Column(IfEntry, 2).ConfigureAwait(false);
        var type = await Column(IfEntry, 3).ConfigureAwait(false);
        var speed = await Column(IfEntry, 5).ConfigureAwait(false);
        var admin = await Column(IfEntry, 7).ConfigureAwait(false);
        var oper = await Column(IfEntry, 8).ConfigureAwait(false);
        var inOctets = await Column(IfEntry, 10).ConfigureAwait(false);
        var inDiscards = await Column(IfEntry, 13).ConfigureAwait(false);
        var inErrors = await Column(IfEntry, 14).ConfigureAwait(false);
        var outOctets = await Column(IfEntry, 16).ConfigureAwait(false);
        var outDiscards = await Column(IfEntry, 19).ConfigureAwait(false);
        var outErrors = await Column(IfEntry, 20).ConfigureAwait(false);
        var name = await Column(IfX, 1).ConfigureAwait(false);
        var hcIn = await Column(IfX, 6).ConfigureAwait(false);
        var hcOut = await Column(IfX, 10).ConfigureAwait(false);
        var highSpeed = await Column(IfX, 15).ConfigureAwait(false);
        var alias = await Column(IfX, 18).ConfigureAwait(false);

        string? S(Dictionary<int, SnmpValue> m, int i) => m.TryGetValue(i, out var v) && !v.IsException ? v.ToString() : null;
        ulong? U(Dictionary<int, SnmpValue> m, int i) => m.TryGetValue(i, out var v) ? v.AsUnsigned() : null;
        long? L(Dictionary<int, SnmpValue> m, int i) => m.TryGetValue(i, out var v) ? v.AsLong() : null;

        var ports = new List<SnmpPort>();
        foreach (var index in descr.Keys.Union(name.Keys).Order())
        {
            var hc = hcIn.ContainsKey(index) && hcOut.ContainsKey(index);
            double? mbps = U(highSpeed, index) is > 0 and var hs ? hs : U(speed, index) is > 0 and var sp ? sp / 1e6 : null;
            ports.Add(new SnmpPort(
                index,
                S(name, index) is { Length: > 0 } n ? n : S(descr, index) ?? $"if{index}",
                S(alias, index) is { Length: > 0 } a ? a : null,
                S(descr, index),
                (int)(L(type, index) ?? 0),
                L(admin, index) == 1,
                L(oper, index) == 1,
                mbps,
                hc ? U(hcIn, index) : U(inOctets, index),
                hc ? U(hcOut, index) : U(outOctets, index),
                hc,
                U(inErrors, index),
                U(outErrors, index),
                U(inDiscards, index),
                U(outDiscards, index)));
        }

        // Processor (average of hrProcessorLoad) and memory (the RAM entry of hrStorage), when the device has them.
        double? cpu = null;
        var loads = await Column(HrProcessorLoad[..HrProcessorLoad.LastIndexOf('.')], 2).ConfigureAwait(false);
        if (loads.Count > 0)
        {
            cpu = loads.Values.Select(v => (double)(v.AsLong() ?? 0)).Average();
        }

        double? memory = null;
        var storageType = await Column(HrStorage, 2).ConfigureAwait(false);
        if (storageType.FirstOrDefault(kv => kv.Value.ToString() == HrStorageRam) is { Value: not null } ram)
        {
            var size = await Column(HrStorage, 5).ConfigureAwait(false);
            var used = await Column(HrStorage, 6).ConfigureAwait(false);
            if (size.TryGetValue(ram.Key, out var s) && used.TryGetValue(ram.Key, out var u) && s.AsLong() is > 0 and var total)
            {
                memory = (u.AsLong() ?? 0) * 100.0 / total;
            }
        }

        return new SnmpPoll(DateTimeOffset.Now, new SnmpSystem(Text(0), Text(1), Text(2), uptime, Text(4), Text(5), cpu, memory), ports);
    }

    /// <summary>Traffic per port between two readings (counter wrap handled for 32-bit counters, resets skipped).</summary>
    public static IReadOnlyList<PortRate> Rates(SnmpPoll previous, SnmpPoll current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var seconds = (current.At - previous.At).TotalSeconds;
        if (seconds <= 0)
        {
            return [];
        }

        var before = previous.Ports.ToDictionary(p => p.Index);
        var rates = new List<PortRate>();
        foreach (var port in current.Ports)
        {
            if (!before.TryGetValue(port.Index, out var old))
            {
                continue;
            }

            double? Delta(ulong? now, ulong? then, bool wide)
            {
                if (now is not { } n || then is not { } t)
                {
                    return null;
                }

                if (n >= t)
                {
                    return n - t;
                }

                return wide ? null : (double)(n + 4294967296UL - t); // 32-bit wrap; a 64-bit "wrap" is a reset
            }

            var din = Delta(port.InOctets, old.InOctets, port.HighCapacity);
            var dout = Delta(port.OutOctets, old.OutOctets, port.HighCapacity);
            var errorsNow = (port.InErrors ?? 0) + (port.OutErrors ?? 0);
            var errorsThen = (old.InErrors ?? 0) + (old.OutErrors ?? 0);
            rates.Add(new PortRate(
                port.Index,
                din is { } i ? i * 8 / seconds / 1e6 : 0,
                dout is { } o ? o * 8 / seconds / 1e6 : 0,
                errorsNow >= errorsThen ? errorsNow - errorsThen : 0));
        }

        return rates;
    }

    /// <summary>What changed between two readings worth an alert.</summary>
    public static IReadOnlyList<SnmpEvent> Changes(string deviceId, SnmpPoll? previous, SnmpPoll current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var events = new List<SnmpEvent>();
        if (previous is null)
        {
            return events;
        }

        if (current.System.Uptime is { } up && previous.System.Uptime is { } before && up < before)
        {
            events.Add(new(current.At, deviceId, SnmpEventKind.Restarted, current.System.Name ?? "", null, true));
        }

        var old = previous.Ports.ToDictionary(p => p.Index);
        var rates = Rates(previous, current).ToDictionary(r => r.Index);
        foreach (var port in current.Ports.Where(p => !p.IsSoftware))
        {
            if (!old.TryGetValue(port.Index, out var was))
            {
                continue;
            }

            if (was.OperUp && !port.OperUp && port.AdminUp)
            {
                events.Add(new(current.At, deviceId, SnmpEventKind.PortDown, port.Label, null, true));
            }
            else if (!was.OperUp && port.OperUp)
            {
                events.Add(new(current.At, deviceId, SnmpEventKind.PortUp, port.Label, port.SpeedMbps is { } s ? Speed(s) : null, false));
            }
            else if (port.OperUp && was.OperUp && port.SpeedMbps is { } now && was.SpeedMbps is { } then && Math.Abs(now - then) > 0.5)
            {
                events.Add(new(current.At, deviceId, SnmpEventKind.SpeedChanged, port.Label, $"{Speed(then)} → {Speed(now)}", now < then));
            }

            if (rates.TryGetValue(port.Index, out var rate) && rate.NewErrors >= 10)
            {
                events.Add(new(current.At, deviceId, SnmpEventKind.Errors, port.Label, rate.NewErrors.ToString(CultureInfo.InvariantCulture), true));
            }
        }

        return events;
    }

    public static string Speed(double mbps) => mbps >= 1000
        ? (mbps / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " Gbps"
        : mbps.ToString("0", CultureInfo.InvariantCulture) + " Mbps";
}
