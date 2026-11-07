using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace HWSuite.Core.Snmp;

public enum SnmpVersion
{
    V1 = 0,
    V2c = 1,
    V3 = 3,
}

/// <summary>Where and how to reach an agent. Passwords are in clear here; they are protected where they are stored.</summary>
public sealed record SnmpTarget(
    string Host,
    int Port = 161,
    SnmpVersion Version = SnmpVersion.V3,
    string? Community = null,
    string? User = null,
    SnmpAuth Auth = SnmpAuth.Sha1,
    string? AuthPassword = null,
    SnmpPriv Priv = SnmpPriv.Aes128,
    string? PrivPassword = null,
    int TimeoutMs = 2000,
    int Retries = 1);

public enum SnmpError
{
    Timeout,
    UnknownUser,
    WrongPassword,
    UnsupportedSecurityLevel,
    NotInTimeWindow,
    DecryptionError,
    AgentError,
    BadResponse,
}

public sealed class SnmpException : Exception
{
    public SnmpException(SnmpError error, string message)
        : base(message) => Error = error;

    public SnmpException()
    {
    }

    public SnmpException(string message)
        : base(message)
    {
    }

    public SnmpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SnmpError Error { get; }
}

/// <summary>
/// SNMP manager for v1, v2c and v3 (USM with MD5/SHA authentication and DES/AES privacy): get, get-next, get-bulk
/// and table walks over UDP. One client talks to one agent; requests are sequential.
/// </summary>
public sealed class SnmpClient : IDisposable
{
    private static readonly string UnknownEngineId = "1.3.6.1.6.3.15.1.1.4.0";
    private static readonly string NotInTimeWindows = "1.3.6.1.6.3.15.1.1.2.0";
    private static readonly string UnknownUserNames = "1.3.6.1.6.3.15.1.1.3.0";
    private static readonly string WrongDigests = "1.3.6.1.6.3.15.1.1.5.0";
    private static readonly string DecryptionErrors = "1.3.6.1.6.3.15.1.1.6.0";
    private static readonly string UnsupportedSecLevels = "1.3.6.1.6.3.15.1.1.1.0";

    private readonly SnmpTarget _target;
    private readonly UdpClient _udp;
    private IPEndPoint? _endpoint;
    private int _requestId = RandomNumberGenerator.GetInt32(1, int.MaxValue / 2);
    private ulong _salt = (ulong)RandomNumberGenerator.GetInt32(int.MaxValue);

    // v3 engine state, learned by discovery.
    private byte[] _engineId = [];
    private uint _boots;
    private uint _time;
    private DateTime _timeAt;
    private byte[]? _authKey;
    private byte[]? _privKey;

    public SnmpClient(SnmpTarget target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _udp = new UdpClient(AddressFamily.InterNetwork);
    }

    /// <summary>The agent's engine id (v3, after the first request), as hex.</summary>
    public string? EngineId => _engineId.Length > 0 ? Convert.ToHexString(_engineId) : null;

    public void Dispose() => _udp.Dispose();

    public Task<IReadOnlyList<Varbind>> GetAsync(IEnumerable<string> oids, CancellationToken cancel = default) =>
        RequestAsync(SnmpTag.GetRequest, [.. oids], 0, 0, cancel);

    public Task<IReadOnlyList<Varbind>> GetNextAsync(IEnumerable<string> oids, CancellationToken cancel = default) =>
        RequestAsync(SnmpTag.GetNextRequest, [.. oids], 0, 0, cancel);

    public Task<IReadOnlyList<Varbind>> GetBulkAsync(IEnumerable<string> oids, int maxRepetitions, CancellationToken cancel = default) =>
        RequestAsync(SnmpTag.GetBulkRequest, [.. oids], 0, maxRepetitions, cancel);

    /// <summary>Every value under <paramref name="root"/> (get-bulk on v2c/v3, get-next on v1), in order.</summary>
    public async Task<IReadOnlyList<Varbind>> WalkAsync(string root, int limit = 5000, CancellationToken cancel = default)
    {
        var prefix = root.TrimStart('.') + ".";
        var result = new List<Varbind>();
        var current = root.TrimStart('.');
        while (result.Count < limit)
        {
            var batch = _target.Version == SnmpVersion.V1
                ? await GetNextAsync([current], cancel).ConfigureAwait(false)
                : await GetBulkAsync([current], 25, cancel).ConfigureAwait(false);
            var progressed = false;
            foreach (var vb in batch)
            {
                if (vb.Value.Tag == SnmpTag.EndOfMibView || !vb.Oid.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return result;
                }

                if (OidCompare(vb.Oid, current) <= 0)
                {
                    return result; // agent going backwards: stop rather than loop
                }

                result.Add(vb);
                current = vb.Oid;
                progressed = true;
            }

            if (!progressed)
            {
                break;
            }
        }

        return result;
    }

    public static int OidCompare(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var c = ulong.Parse(x[i], System.Globalization.CultureInfo.InvariantCulture).CompareTo(ulong.Parse(y[i], System.Globalization.CultureInfo.InvariantCulture));
            if (c != 0)
            {
                return c;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    private async Task<IReadOnlyList<Varbind>> RequestAsync(byte pduType, string[] oids, int nonRepeaters, int maxRepetitions, CancellationToken cancel)
    {
        _endpoint ??= await ResolveAsync(cancel).ConfigureAwait(false);
        if (_target.Version != SnmpVersion.V3)
        {
            var pdu = Pdu(pduType, NextId(), oids, nonRepeaters, maxRepetitions);
            var message = Ber.Sequence(SnmpTag.Sequence, Ber.Number((int)_target.Version), Ber.Octets(_target.Community ?? "public"), pdu);
            var reply = await ExchangeAsync(message, cancel).ConfigureAwait(false);
            return ParseCommunityReply(reply);
        }

        if (_engineId.Length == 0)
        {
            await DiscoverAsync(cancel).ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            var (msgId, message) = BuildV3(pduType, oids, nonRepeaters, maxRepetitions);
            var reply = await ExchangeAsync(message, cancel, msgId).ConfigureAwait(false);
            var (type, varbinds) = ParseV3Reply(reply);
            if (type == SnmpTag.Report)
            {
                var oid = varbinds.Count > 0 ? varbinds[0].Oid : null;
                if (oid == NotInTimeWindows && attempt == 0)
                {
                    continue; // ParseV3Reply took the agent's boots/time: try once more
                }

                if (oid == UnknownEngineId && attempt == 0)
                {
                    continue;
                }

                throw oid switch
                {
                    _ when oid == UnknownUserNames => new SnmpException(SnmpError.UnknownUser, "Unknown SNMPv3 user"),
                    _ when oid == WrongDigests => new SnmpException(SnmpError.WrongPassword, "Wrong authentication password or protocol"),
                    _ when oid == DecryptionErrors => new SnmpException(SnmpError.DecryptionError, "Wrong privacy password or protocol"),
                    _ when oid == UnsupportedSecLevels => new SnmpException(SnmpError.UnsupportedSecurityLevel, "Security level not accepted by the agent"),
                    _ when oid == NotInTimeWindows => new SnmpException(SnmpError.NotInTimeWindow, "Clock out of the agent's time window"),
                    _ => new SnmpException(SnmpError.BadResponse, "Report " + oid),
                };
            }

            return varbinds;
        }
    }

    private async Task<IPEndPoint> ResolveAsync(CancellationToken cancel)
    {
        if (IPAddress.TryParse(_target.Host, out var ip))
        {
            return new IPEndPoint(ip, _target.Port);
        }

        var addresses = await Dns.GetHostAddressesAsync(_target.Host, AddressFamily.InterNetwork, cancel).ConfigureAwait(false);
        return addresses.Length > 0 ? new IPEndPoint(addresses[0], _target.Port) : throw new SnmpException(SnmpError.Timeout, "Host not found");
    }

    private int NextId() => Interlocked.Increment(ref _requestId) & 0x7FFFFFFF;

    private static byte[] Pdu(byte type, int requestId, string[] oids, int nonRepeaters, int maxRepetitions)
    {
        var list = oids.Select(o => Ber.Sequence(SnmpTag.Sequence, Ber.Oid(o), Ber.NullValue())).ToArray();
        return Ber.Sequence(type, Ber.Number(requestId), Ber.Number(nonRepeaters), Ber.Number(maxRepetitions), Ber.Sequence(SnmpTag.Sequence, list));
    }

    private async Task<byte[]> ExchangeAsync(byte[] message, CancellationToken cancel, int? msgId = null)
    {
        for (var attempt = 0; attempt <= _target.Retries; attempt++)
        {
            await _udp.SendAsync(message, _endpoint!, cancel).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(_target.TimeoutMs);
            try
            {
                while (true)
                {
                    var received = await _udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (msgId is null || MessageId(received.Buffer) == msgId)
                    {
                        return received.Buffer;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                // try again
            }
            catch (SocketException)
            {
                // ICMP port unreachable and friends: same as no answer
            }
        }

        throw new SnmpException(SnmpError.Timeout, "No answer");
    }

    private static int? MessageId(byte[] data)
    {
        try
        {
            var outer = Ber.Read(data, 0);
            var parts = Ber.Children(data, outer.Start, outer.Length);
            if (parts.Count < 2 || Ber.ReadSigned(data.AsSpan(parts[0].Start, parts[0].Length)) != 3)
            {
                return null;
            }

            var header = Ber.Children(data, parts[1].Start, parts[1].Length);
            return (int)Ber.ReadSigned(data.AsSpan(header[0].Start, header[0].Length));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ---- v1 / v2c ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<Varbind> ParseCommunityReply(byte[] data)
    {
        var outer = Ber.Read(data, 0);
        var parts = Ber.Children(data, outer.Start, outer.Length);
        if (parts.Count < 3)
        {
            throw new SnmpException(SnmpError.BadResponse, "Bad SNMP message");
        }

        return ParsePdu(data, parts[2]).Varbinds;
    }

    private static (byte Type, IReadOnlyList<Varbind> Varbinds) ParsePdu(byte[] data, (byte Tag, int Start, int Length) pdu)
    {
        var fields = Ber.Children(data, pdu.Start, pdu.Length);
        if (fields.Count < 4)
        {
            throw new SnmpException(SnmpError.BadResponse, "Bad PDU");
        }

        var status = Ber.ReadSigned(data.AsSpan(fields[1].Start, fields[1].Length));
        if (status != 0 && pdu.Tag == SnmpTag.Response)
        {
            // v1 noSuchName (2) on get-next means "end of the MIB": report it as an empty answer.
            if (status == 2)
            {
                return (pdu.Tag, []);
            }

            throw new SnmpException(SnmpError.AgentError, StatusName(status));
        }

        var list = new List<Varbind>();
        foreach (var vb in Ber.Children(data, fields[3].Start, fields[3].Length))
        {
            var pair = Ber.Children(data, vb.Start, vb.Length);
            if (pair.Count < 2)
            {
                continue;
            }

            var oid = Ber.ReadOid(data.AsSpan(pair[0].Start, pair[0].Length));
            list.Add(new Varbind(oid, new SnmpValue(pair[1].Tag, data.AsSpan(pair[1].Start, pair[1].Length).ToArray())));
        }

        return (pdu.Tag, list);
    }

    private static string StatusName(long status) => status switch
    {
        1 => "tooBig",
        2 => "noSuchName",
        3 => "badValue",
        4 => "readOnly",
        5 => "genErr",
        6 => "noAccess",
        16 => "authorizationError",
        _ => "error " + status,
    };

    // ---- v3 ---------------------------------------------------------------------------------------------------

    private async Task DiscoverAsync(CancellationToken cancel)
    {
        var msgId = NextId();
        var secParams = Ber.Sequence(SnmpTag.Sequence, Ber.Octets([]), Ber.Number(0), Ber.Number(0), Ber.Octets([]), Ber.Octets([]), Ber.Octets([]));
        var scoped = Ber.Sequence(SnmpTag.Sequence, Ber.Octets([]), Ber.Octets([]), Pdu(SnmpTag.GetRequest, NextId(), [], 0, 0));
        var message = Ber.Sequence(SnmpTag.Sequence,
            Ber.Number(3),
            Ber.Sequence(SnmpTag.Sequence, Ber.Number(msgId), Ber.Number(65507), Ber.Octets([4]), Ber.Number(3)),
            Ber.Octets(secParams),
            scoped);
        var reply = await ExchangeAsync(message, cancel, msgId).ConfigureAwait(false);
        ReadSecurity(reply, out _, out _, out _);
        if (_engineId.Length == 0)
        {
            throw new SnmpException(SnmpError.BadResponse, "Engine discovery failed");
        }
    }

    private void Keys()
    {
        if (_authKey is not null || _target.Auth == SnmpAuth.None)
        {
            return;
        }

        _authKey = Usm.Localize(_target.Auth, Usm.PasswordToKey(_target.Auth, _target.AuthPassword ?? ""), _engineId);
        if (_target.Priv != SnmpPriv.None)
        {
            _privKey = Usm.PrivKey(_target.Auth, _target.Priv, _target.PrivPassword ?? _target.AuthPassword ?? "", _engineId);
        }
    }

    private uint AgentTime => _time + (uint)Math.Max(0, (DateTime.UtcNow - _timeAt).TotalSeconds);

    private (int MsgId, byte[] Message) BuildV3(byte pduType, string[] oids, int nonRepeaters, int maxRepetitions)
    {
        Keys();
        var auth = _target.Auth != SnmpAuth.None;
        var priv = auth && _target.Priv != SnmpPriv.None;
        var msgId = NextId();
        var flags = (byte)((auth ? 1 : 0) | (priv ? 2 : 0) | 4);
        var time = AgentTime;
        var scoped = Ber.Sequence(SnmpTag.Sequence, Ber.Octets(_engineId), Ber.Octets([]), Pdu(pduType, NextId(), oids, nonRepeaters, maxRepetitions));
        byte[] privParams = [];
        byte[] data = scoped;
        if (priv)
        {
            var encrypted = Usm.Encrypt(_target.Priv, _privKey!, scoped, _boots, time, ++_salt, out privParams);
            data = Ber.Octets(encrypted);
        }

        var macLength = Usm.MacLength(_target.Auth);
        var placeholder = new byte[macLength];
        var secParams = Ber.Sequence(SnmpTag.Sequence,
            Ber.Octets(_engineId), Ber.Number(_boots), Ber.Number(time), Ber.Octets(_target.User ?? ""), Ber.Octets(placeholder), Ber.Octets(privParams));
        var header = Ber.Sequence(SnmpTag.Sequence, Ber.Number(msgId), Ber.Number(65507), Ber.Octets([flags]), Ber.Number(3));
        var version = Ber.Number(3);
        var secOctets = Ber.Octets(secParams);
        var message = Ber.Sequence(SnmpTag.Sequence, version, header, secOctets, data);
        if (auth)
        {
            // The MAC goes where the zero placeholder is: inside the security parameters.
            var secStart = IndexOf(message, secOctets);
            var macAt = secStart + secOctets.Length - secParams.Length + AuthOffset(secParams);
            var mac = Usm.Hmac(_target.Auth, _authKey!, message)[..macLength];
            mac.CopyTo(message, macAt);
        }

        return (msgId, message);
    }

    /// <summary>Where the authentication parameter's content starts within the security parameters sequence.</summary>
    private static int AuthOffset(byte[] secParams)
    {
        var outer = Ber.Read(secParams, 0);
        var fields = Ber.Children(secParams, outer.Start, outer.Length);
        return fields[4].Start;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Security parameters not found");
    }

    /// <summary>Reads the security parameters of a v3 reply, keeping the agent's engine id, boots and time.</summary>
    private static (int SecStart, int SecLength, byte Flags) ReadSecurityParts(byte[] data, out List<(byte Tag, int Start, int Length)> parts)
    {
        var outer = Ber.Read(data, 0);
        parts = Ber.Children(data, outer.Start, outer.Length);
        if (parts.Count < 4 || Ber.ReadSigned(data.AsSpan(parts[0].Start, parts[0].Length)) != 3)
        {
            throw new SnmpException(SnmpError.BadResponse, "Not an SNMPv3 message");
        }

        var header = Ber.Children(data, parts[1].Start, parts[1].Length);
        var flags = data[header[2].Start];
        return (parts[2].Start, parts[2].Length, flags);
    }

    private void ReadSecurity(byte[] data, out byte[] authParams, out byte[] privParams, out byte flags)
    {
        var (start, length, f) = ReadSecurityParts(data, out _);
        flags = f;
        var seq = Ber.Read(data, start);
        var fields = Ber.Children(data, seq.Start, seq.Length);
        _engineId = data.AsSpan(fields[0].Start, fields[0].Length).ToArray();
        _boots = (uint)Ber.ReadSigned(data.AsSpan(fields[1].Start, fields[1].Length));
        _time = (uint)Ber.ReadSigned(data.AsSpan(fields[2].Start, fields[2].Length));
        _timeAt = DateTime.UtcNow;
        authParams = data.AsSpan(fields[4].Start, fields[4].Length).ToArray();
        privParams = data.AsSpan(fields[5].Start, fields[5].Length).ToArray();
        _ = length;
    }

    private (byte Type, IReadOnlyList<Varbind> Varbinds) ParseV3Reply(byte[] data)
    {
        var engineBefore = _engineId;
        ReadSecurity(data, out var authParams, out var privParams, out var flags);
        if (!engineBefore.AsSpan().SequenceEqual(_engineId))
        {
            _authKey = null; // keys are localized to the engine id
            _privKey = null;
            Keys();
        }

        var (_, _, _) = ReadSecurityParts(data, out var parts);

        // Verify the MAC when the reply is authenticated.
        if ((flags & 1) != 0 && _authKey is not null && authParams.Length == Usm.MacLength(_target.Auth))
        {
            var copy = (byte[])data.Clone();
            var macAt = IndexOf(copy, authParams);
            Array.Clear(copy, macAt, authParams.Length);
            var expected = Usm.Hmac(_target.Auth, _authKey, copy)[..authParams.Length];
            if (!CryptographicOperations.FixedTimeEquals(expected, authParams))
            {
                throw new SnmpException(SnmpError.WrongPassword, "Reply authentication failed");
            }
        }

        var payload = parts[3];
        List<(byte Tag, int Start, int Length)> fields;
        byte[] scoped;
        if ((flags & 2) != 0)
        {
            if (_privKey is null)
            {
                throw new SnmpException(SnmpError.DecryptionError, "Encrypted reply without a privacy key");
            }

            scoped = Usm.Decrypt(_target.Priv, _privKey, data.AsSpan(payload.Start, payload.Length).ToArray(), _boots, _time, privParams);
            var seq = Ber.Read(scoped, 0);
            fields = Ber.Children(scoped, seq.Start, seq.Length);
        }
        else
        {
            scoped = data;
            fields = Ber.Children(data, payload.Start, payload.Length);
        }

        if (fields.Count < 3)
        {
            throw new SnmpException(SnmpError.BadResponse, "Bad scoped PDU");
        }

        return ParsePdu(scoped, fields[2]);
    }
}
