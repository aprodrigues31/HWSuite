using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HWSuite.Core.Network;

/// <summary>
/// What both probes share: the server address pinned for one IP version, round trips as TCP handshakes to it,
/// and an HTTP handler whose every connection goes to that address.
/// </summary>
public abstract class PinnedProbe : INetworkProbe
{
    private SocketsHttpHandler? _handler;

    protected PinnedProbe(IpFamily family)
    {
        Family = family;
    }

    public IpFamily Family { get; }

    public NetEndpoint Endpoint { get; protected set; } = new("", null, null, null);

    /// <summary>The server address every connection uses (set by <see cref="PinAsync"/>).</summary>
    protected IPAddress? Address { get; private set; }

    protected int Port { get; private set; } = 443;

    protected static string UserAgent => $"HWSuite/{BuildInfo.Current.Version}";

    /// <summary>Handler that connects to <see cref="Address"/> whatever the host name says (TLS still checks the name).</summary>
    protected SocketsHttpHandler Handler => _handler ??= new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        MaxConnectionsPerServer = 32,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = async (context, cancel) =>
        {
            var socket = new Socket(Address!.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(Address, context.DnsEndPoint.Port), cancel).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public abstract Task PrepareAsync(CancellationToken cancel);

    public abstract Task DownloadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel);

    public abstract Task UploadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel);

    /// <summary>Resolves the host for the chosen IP version and checks a TCP handshake works.</summary>
    protected async Task PinAsync(string host, int port, CancellationToken cancel)
    {
        Port = port;
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, Family == IpFamily.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, cancel).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new NetworkTestException(Family == IpFamily.IPv6 ? "net.error.noIpv6" : "net.error.unreachable", ex);
        }

        if (addresses.Length == 0)
        {
            throw new NetworkTestException(Family == IpFamily.IPv6 ? "net.error.noIpv6" : "net.error.unreachable");
        }

        foreach (var address in addresses)
        {
            Address = address;
            if (await RttAsync(cancel).ConfigureAwait(false) is not null)
            {
                return;
            }
        }

        throw new NetworkTestException(Family == IpFamily.IPv6 ? "net.error.noIpv6" : "net.error.unreachable");
    }

    public async Task<double?> RttAsync(CancellationToken cancel)
    {
        if (Address is null)
        {
            return null;
        }

        // One second without a SYN-ACK counts as a lost packet (Windows only resends the SYN after that).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var socket = new Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var clock = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(Address, Port), timeout.Token).ConfigureAwait(false);
            return clock.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return null; // timed out
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _handler?.Dispose();
        }
    }

    /// <summary>Random (incompressible) bytes to send.</summary>
    protected static byte[] RandomBlock(int length)
    {
        var data = new byte[length];
        Random.Shared.NextBytes(data);
        return data;
    }
}

/// <summary>
/// Cloudflare's speed test endpoints (the ones speed.cloudflare.com uses): /meta for the provider and data
/// center, /__down?bytes=N to receive, /__up to send. Six parallel connections download and four upload, so a
/// single TCP connection's window does not cap fast links.
/// </summary>
public sealed class CloudflareProbe : PinnedProbe
{
    public const string Host = "speed.cloudflare.com";
    private const int DownloadStreams = 6;
    private const int UploadStreams = 4;
    private const long DownloadChunk = 25_000_000;
    private const int UploadChunk = 10_000_000;
    private HttpClient? _http;

    public CloudflareProbe(IpFamily family)
        : base(family)
    {
    }

    private HttpClient Http => _http ??= new HttpClient(Handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    public override async Task PrepareAsync(CancellationToken cancel)
    {
        await PinAsync(Host, 443, cancel).ConfigureAwait(false);
        Endpoint = new NetEndpoint(Host, "Cloudflare", null, null);
        if (await GetTextAsync("/meta", cancel).ConfigureAwait(false) is { } meta && ParseMeta(meta) is { Server: not "Cloudflare" } fromMeta)
        {
            Endpoint = fromMeta;
        }
        else if (await GetTextAsync("/cdn-cgi/trace", cancel).ConfigureAwait(false) is { } trace)
        {
            Endpoint = ParseTrace(trace);
        }
    }

    /// <summary>A small GET (sent as the speed test page sends it); null when it fails. The test itself works without these.</summary>
    private async Task<string?> GetTextAsync(string path, CancellationToken cancel)
    {
        try
        {
            using var request = Request(HttpMethod.Get, path);
            request.Headers.Referrer = new Uri($"https://{Host}/");
            request.Headers.Add("Origin", $"https://{Host}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancel.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads /meta: {"clientIp","asOrganization","city","country", "colo": "GRU" or {"iata":"GRU","city":"São Paulo","cca2":"BR"}}.
    /// The data center is shown as "Cloudflare São Paulo, BR (GRU)".
    /// </summary>
    public static NetEndpoint ParseMeta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new NetEndpoint(Host, "Cloudflare", null, null);
            }

            static string? Get(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
            string? colo, city, country;
            if (root.TryGetProperty("colo", out var c) && c.ValueKind == JsonValueKind.Object)
            {
                (colo, city, country) = (Get(c, "iata"), Get(c, "city"), Get(c, "cca2"));
            }
            else
            {
                (colo, city, country) = (Get(root, "colo"), Get(root, "city"), Get(root, "country"));
            }

            return new NetEndpoint(Host, Label(colo, city, country), Get(root, "asOrganization"), Get(root, "clientIp"));
        }
        catch (JsonException)
        {
            return new NetEndpoint(Host, "Cloudflare", null, null);
        }
    }

    /// <summary>Reads /cdn-cgi/trace (key=value lines: ip, colo, loc), the fallback when /meta is unavailable.</summary>
    public static NetEndpoint ParseTrace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.Ordinal);
        return new NetEndpoint(Host, Label(values.GetValueOrDefault("colo"), null, values.GetValueOrDefault("loc")), null, values.GetValueOrDefault("ip"));
    }

    private static string Label(string? colo, string? city, string? country)
    {
        var place = string.Join(", ", new[] { city, country }.OfType<string>());
        return "Cloudflare" + (place.Length > 0 ? " " + place : "") + (colo is null ? "" : $" ({colo})");
    }

    public override Task DownloadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel) =>
        Task.WhenAll(Enumerable.Range(0, DownloadStreams).Select(_ => Task.Run(async () =>
        {
            var buffer = new byte[1 << 16];
            while (!cancel.IsCancellationRequested)
            {
                using var request = Request(HttpMethod.Get, $"/__down?bytes={DownloadChunk}");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                int read;
                while ((read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    onBytes(read);
                }
            }
        }, cancel)));

    public override Task UploadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel)
    {
        var block = RandomBlock(1 << 16);
        return Task.WhenAll(Enumerable.Range(0, UploadStreams).Select(_ => Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                using var request = Request(HttpMethod.Post, "/__up");
                request.Content = new CountingContent(block, UploadChunk, onBytes);
                using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
        }, cancel)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _http?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri($"https://{Host}{path}"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HWSuite", BuildInfo.Current.Version));
        return request;
    }

    /// <summary>A request body of <c>length</c> bytes written from one block, reporting each write.</summary>
    private sealed class CountingContent(byte[] block, int length, Action<long> onBytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            for (var sent = 0; sent < length; sent += block.Length)
            {
                var count = Math.Min(block.Length, length - sent);
                await stream.WriteAsync(block.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                onBytes(count);
            }
        }

        protected override bool TryComputeLength(out long length1)
        {
            length1 = length;
            return true;
        }
    }
}

/// <summary>
/// Measurement Lab's ndt7 protocol (github.com/m-lab/ndt-server, spec/ndt7-protocol.md): the Locate API v2
/// picks the nearest healthy server, then one WebSocket per direction carries binary messages for up to ten
/// seconds. M-Lab publishes every measurement (with the client's public IP) as open data, so the app asks for
/// consent before using it.
/// </summary>
public sealed class MlabProbe : PinnedProbe
{
    public const string SubProtocol = "net.measurementlab.ndt.v7";
    public static readonly Uri PrivacyPolicy = new("https://www.measurementlab.net/privacy/");
    private static readonly Uri Locate = new("https://locate.measurementlab.net/v2/nearest/ndt/ndt7");
    private Uri? _download;
    private Uri? _upload;

    public MlabProbe(IpFamily family)
        : base(family)
    {
    }

    /// <summary>The nearest server as the Locate API returned it.</summary>
    public sealed record LocatedServer(string Machine, string? City, string? Country, Uri Download, Uri Upload);

    public override async Task PrepareAsync(CancellationToken cancel)
    {
        LocatedServer server;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{Locate}?client_name=hwsuite&client_version={Uri.EscapeDataString(BuildInfo.Current.Version)}"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HWSuite", BuildInfo.Current.Version));
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                throw new NetworkTestException("net.error.unreachable", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NoContent || (int)response.StatusCode == 429)
                {
                    throw new NetworkTestException("net.error.rateLimited");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new NetworkTestException("net.error.unreachable");
                }

                server = ParseLocate(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false))
                    ?? throw new NetworkTestException("net.error.unreachable");
            }
        }

        _download = server.Download;
        _upload = server.Upload;
        await PinAsync(server.Download.Host, server.Download.Port, cancel).ConfigureAwait(false);
        var place = string.Join(", ", new[] { server.City, server.Country }.OfType<string>());
        Endpoint = new NetEndpoint(server.Download.Host, $"M-Lab {Site(server.Machine)}" + (place.Length > 0 ? $" — {place}" : ""), null, null);
    }

    /// <summary>The first result of a Locate v2 answer: {"results":[{"machine","location":{"city","country"},"urls":{...}}]}.</summary>
    public static LocatedServer? ParseLocate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var r in results.EnumerateArray())
            {
                if (!r.TryGetProperty("urls", out var urls)
                    || !urls.TryGetProperty("wss:///ndt/v7/download", out var down) || !urls.TryGetProperty("wss:///ndt/v7/upload", out var up)
                    || !Uri.TryCreate(down.GetString(), UriKind.Absolute, out var downUri) || !Uri.TryCreate(up.GetString(), UriKind.Absolute, out var upUri))
                {
                    continue;
                }

                string? city = null, country = null;
                if (r.TryGetProperty("location", out var location))
                {
                    city = location.TryGetProperty("city", out var c) ? c.GetString() : null;
                    country = location.TryGetProperty("country", out var k) ? k.GetString() : null;
                }

                return new LocatedServer(r.TryGetProperty("machine", out var m) ? m.GetString() ?? downUri.Host : downUri.Host, city, country, downUri, upUri);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"mlab1-gru05.mlab-oti.measurement-lab.org" → "mlab1-gru05".</summary>
    public static string Site(string machine) => machine.Split('.')[0];

    /// <summary>The client address in an ndt7 measurement ("ConnectionInfo":{"Client":"[2001:db8::1]:51234"}), without the port.</summary>
    public static string? ClientAddress(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ConnectionInfo", out var info) || !info.TryGetProperty("Client", out var client) || client.GetString() is not { Length: > 0 } text)
            {
                return null;
            }

            if (text.StartsWith('['))
            {
                var end = text.IndexOf(']', StringComparison.Ordinal);
                return end > 1 ? text[1..end] : null;
            }

            var colon = text.LastIndexOf(':');
            return colon > 0 ? text[..colon] : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The ndt7 upload message size: starts at 8 KiB and doubles while it is under 1/16 of what was sent (capped at 4 MiB).</summary>
    public static int NextMessageSize(int current, long totalSent) =>
        current < (1 << 22) && current < totalSent / 16 ? current * 2 : current;

    public override async Task DownloadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel)
    {
        using var ws = await ConnectAsync(_download!, cancel).ConfigureAwait(false);
        var buffer = new byte[1 << 20];
        var text = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseQuietlyAsync(ws, cancel).ConfigureAwait(false);
                return;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                onBytes(result.Count);
            }
            else
            {
                text.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    if (Endpoint.ClientAddress is null && ClientAddress(Encoding.UTF8.GetString(text.ToArray())) is { } client)
                    {
                        Endpoint = Endpoint with { ClientAddress = client };
                    }

                    text.SetLength(0);
                }
            }
        }
    }

    public override async Task UploadAsync(Action<long> onBytes, TimeSpan duration, CancellationToken cancel)
    {
        using var ws = await ConnectAsync(_upload!, cancel).ConfigureAwait(false);
        var data = RandomBlock(1 << 22);
        var size = 1 << 13;
        long sent = 0;

        // The server's measurements (and pings, answered while a receive is pending) arrive during the upload.
        var receive = Task.Run(async () =>
        {
            var buffer = new byte[1 << 16];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
            }
        }, cancel);

        var clock = Stopwatch.StartNew();
        while (ws.State == WebSocketState.Open && clock.Elapsed < duration && !receive.IsCompleted)
        {
            await ws.SendAsync(data.AsMemory(0, size), WebSocketMessageType.Binary, true, cancel).ConfigureAwait(false);
            sent += size;
            onBytes(size);
            size = NextMessageSize(size, sent);
        }

        await CloseQuietlyAsync(ws, cancel).ConfigureAwait(false);
        try
        {
            await receive.ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(Uri uri, CancellationToken cancel)
    {
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(SubProtocol);
        ws.Options.SetRequestHeader("User-Agent", UserAgent);
        try
        {
            using var invoker = new HttpMessageInvoker(Handler, disposeHandler: false);
            await ws.ConnectAsync(uri, invoker, cancel).ConfigureAwait(false);
            return ws;
        }
        catch
        {
            ws.Dispose();
            throw;
        }
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket ws, CancellationToken cancel)
    {
        try
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
        }
    }
}
