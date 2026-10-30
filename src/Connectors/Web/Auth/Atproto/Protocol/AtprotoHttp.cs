using System.Net;
using System.Net.Sockets;
using Koan.Core;
using Koan.Web.Auth.Connector.Atproto.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// Connection-time address validation: DNS results are vetted and that exact address is connected. Protocol documents
/// the connector reads itself — discovery, identity, the pushed request, tokens, revocation — are bounded by
/// <see cref="AtprotoOptions.MaximumProtocolResponseBytes"/>; a response sent for a caller streams, within the limit
/// the caller sets.
/// </summary>
public sealed class AtprotoHttp : IDisposable
{
    private readonly AtprotoOptions options;
    private readonly bool development;
    private readonly SocketsHttpHandler sockets;
    private readonly HttpClient streaming;

    /// <summary>The bounded, buffered client for protocol documents the connector reads itself.</summary>
    internal HttpClient Client { get; }

    private static readonly IPNetwork[] NonPublicV4 = [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"), IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.0.2.0/24"), IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"), IPNetwork.Parse("224.0.0.0/3")];
    private static readonly IPNetwork[] NonPublicV6 = [IPNetwork.Parse("2001::/23"), IPNetwork.Parse("2001:db8::/32"), IPNetwork.Parse("2002::/16"), IPNetwork.Parse("3fff::/20")];

    public AtprotoHttp(IOptions<AtprotoOptions> configured, IHostEnvironment environment)
    {
        options = configured.Value;
        development = KoanEnv.Gate.DevelopmentOnly(environment);
        sockets = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = Connect };
        var timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        Client = new HttpClient(new BoundedHandler(this, sockets), disposeHandler: false) { Timeout = timeout };
        streaming = new HttpClient(new GuardedHandler(this, sockets), disposeHandler: false) { Timeout = timeout };
    }

    internal bool DevelopmentOrigin(Uri uri) => development && options.DevelopmentAllowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
    internal string ConnectionHost(Uri uri) => DevelopmentOrigin(uri) && options.DevelopmentConnectHost is { } host ? host : uri.Host;

    internal void Validate(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 ||
            (uri.Scheme != "https" && !DevelopmentOrigin(uri))) throw new InvalidOperationException("Unsafe AT Protocol destination.");
        if (!DevelopmentOrigin(uri) && (uri.Scheme != "https" || !uri.IsDefaultPort))
            throw new InvalidOperationException("Public AT Protocol discovery requires HTTPS on the default port.");
    }

    internal static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork) return !NonPublicV4.Any(x => x.Contains(address));
        return address.AddressFamily == AddressFamily.InterNetworkV6 && IPNetwork.Parse("2000::/3").Contains(address) && !NonPublicV6.Any(x => x.Contains(address));
    }

    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var uri = context.InitialRequestMessage.RequestUri!;
        Validate(uri);
        var addresses = await Dns.GetHostAddressesAsync(ConnectionHost(uri), ct);
        if (addresses.Length == 0 || (!DevelopmentOrigin(uri) && addresses.Any(x => !PublicAddress(x))))
            throw new InvalidOperationException("AT Protocol destination resolves to a non-public or unavailable address.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
        }
        throw new HttpRequestException("Unable to connect to the verified AT Protocol destination.");
    }

    public void Dispose()
    {
        Client.Dispose();
        streaming.Dispose();
        sockets.Dispose();
    }

    /// <summary>
    /// Send a caller-owned protocol request through the same address and redirect guards. The response is returned once
    /// its headers arrive, within the request timeout, and its body streams as the caller reads it: no more than
    /// <paramref name="maximumResponseBytes"/> of it, or all of it when that is null, for a caller that streams. A body
    /// the response declares longer than the limit is refused before it is read, and one that runs past it fails the
    /// read that crosses it. The caller owns authentication context and response disposal.
    /// </summary>
    /// <exception cref="HttpRequestException">The response declares a body longer than <paramref name="maximumResponseBytes"/>.</exception>
    public async Task<HttpResponseMessage> Send(HttpRequestMessage request, long? maximumResponseBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (maximumResponseBytes is < 0) throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes), "A response limit is zero or more bytes.");
        var response = await streaming.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (maximumResponseBytes is not { } limit) return response;
        try
        {
            if (response.Content.Headers.ContentLength is { } declared && declared > limit)
                throw new HttpRequestException($"The AT Protocol response declares {declared} bytes, more than the {limit} the caller reads.");
            var body = await response.Content.ReadAsStreamAsync(ct);
            var bounded = new StreamContent(new BoundedStream(body, limit));
            foreach (var header in response.Content.Headers) bounded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = bounded;
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    /// <summary>A protocol document the connector reads itself, bounded and buffered like discovery.</summary>
    internal Task<HttpResponseMessage> Fetch(HttpRequestMessage request, CancellationToken ct) => Client.SendAsync(request, ct);

    private class GuardedHandler(AtprotoHttp owner, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected AtprotoHttp Owner => owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            owner.Validate(request.RequestUri!);
            return base.SendAsync(request, ct);
        }
    }

    private sealed class BoundedHandler(AtprotoHttp owner, HttpMessageHandler inner) : GuardedHandler(owner, inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (output.Length + read > Owner.options.MaximumProtocolResponseBytes) throw new HttpRequestException("AT Protocol response exceeded the configured byte bound.");
                    output.Write(buffer, 0, read);
                }
                var replacement = new ByteArrayContent(output.ToArray());
                foreach (var header in response.Content.Headers) replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
                response.Content.Dispose();
                response.Content = replacement;
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }

    /// <summary>A response body read no further than its limit: the read that would cross it fails instead.</summary>
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        private long read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = inner.Read(buffer[..Allowed(buffer.Length)]);
            return Counted(count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken);
            return Counted(count);
        }

        // One byte past the limit is read when there is one, so a body exactly at the limit is accepted and a longer one is
        // seen. What remains is never negative, since a read past the limit already failed, and it is compared before
        // anything is added to it, so no limit up to long.MaxValue overflows.
        private int Allowed(int requested)
        {
            var remaining = limit - read;
            return remaining >= requested ? requested : (int)remaining + 1;
        }

        private int Counted(int count)
        {
            read += count;
            if (read > limit) throw new IOException($"The AT Protocol response is longer than the {limit} bytes the caller reads.");
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
