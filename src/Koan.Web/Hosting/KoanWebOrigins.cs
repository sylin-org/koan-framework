using System.Collections.Concurrent;
using System.Net;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0081: the origins requests arrive on, other than the base URL in force. <see cref="KoanWebStartupFilter"/> notes
/// each request's origin right after the trusted-proxy posture resolved its scheme and host, so a declared proxy's
/// <c>X-Forwarded-Proto</c> and <c>X-Forwarded-Host</c> count and an undeclared one's do not. An observed origin is only
/// a candidate: nothing here changes the base URL, which becomes another origin only when the application accepts it
/// (<see cref="KoanWebBaseUrl.Accept"/>).
/// </summary>
/// <remarks>
/// The record is held in memory and never persisted. It holds at most <c>Koan:Web:ObservedOriginLimit</c> distinct
/// origins; a new one beyond that evicts the one seen least recently. A request on the base URL in force costs a
/// comparison. Any other request — every request, while nothing is configured or accepted — costs a lookup and two
/// interlocked writes, and a lock only when its origin is new.
/// </remarks>
public sealed class KoanWebOrigins
{
    // RFC 1918 private, RFC 6598 shared, RFC 3927 link-local, RFC 4193 unique local and RFC 4291 link-local ranges.
    private static readonly System.Net.IPNetwork[] PrivateNetworks =
    [
        System.Net.IPNetwork.Parse("10.0.0.0/8"),
        System.Net.IPNetwork.Parse("172.16.0.0/12"),
        System.Net.IPNetwork.Parse("192.168.0.0/16"),
        System.Net.IPNetwork.Parse("100.64.0.0/10"),
        System.Net.IPNetwork.Parse("169.254.0.0/16"),
        System.Net.IPNetwork.Parse("fc00::/7"),
        System.Net.IPNetwork.Parse("fe80::/10"),
    ];

    private readonly KoanWebBaseUrl _baseUrl;
    private readonly int _limit;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _admitting = new();
    private BaseOrigin? _base;

    /// <summary>Observe the origins requests arrive on, holding at most <paramref name="limit"/> of them.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="limit"/> is below one.</exception>
    public KoanWebOrigins(KoanWebBaseUrl baseUrl, int limit, TimeProvider? time = null, ILogger<KoanWebOrigins>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (limit < 1)
        {
            throw new InvalidOperationException(
                $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ObservedOriginLimit} is how many distinct " +
                $"origins Koan holds while it observes where requests arrive; it must be at least 1. It is {limit}.");
        }

        _baseUrl = baseUrl;
        _limit = limit;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Raised once when an origin enters the record: on the request that first arrived on it, before that request goes
    /// on. Keep a handler short and hand longer work off; one that throws is logged and does not fail the request. An
    /// origin evicted as least recent and seen again enters again.
    /// </summary>
    public event EventHandler<ObservedOrigin>? Appeared;

    /// <summary>
    /// The origins seen, most recent first: at most <c>Koan:Web:ObservedOriginLimit</c> of them, without the base URL in
    /// force. Reading takes no lock and writes nothing.
    /// </summary>
    public IReadOnlyList<ObservedOrigin> Seen()
    {
        var baseOrigin = Base()?.Origin;
        // Enumerating the dictionary itself takes no lock; its Values property would take every bucket's.
        return _entries
            .Where(pair => !string.Equals(pair.Key, baseOrigin, StringComparison.Ordinal))
            .Select(static pair => pair.Value.Snapshot())
            .OrderByDescending(static origin => origin.LastSeen)
            .ToArray();
    }

    /// <summary>Note the origin <paramref name="request"/> arrived on, unless it is the base URL in force.</summary>
    internal void Note(HttpRequest request)
    {
        if (!request.Host.HasValue) return;

        var @base = Base();
        if (@base is not null
            && string.Equals(request.Host.Value, @base.Authority, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Scheme, @base.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var origin = KoanWebBaseUrl.OriginOf(request);
        if (origin is null || string.Equals(origin, @base?.Origin, StringComparison.Ordinal)) return;

        Record(origin);
    }

    /// <summary>The class of the host in <paramref name="origin"/>, an absolute http(s) origin.</summary>
    internal static OriginAddressClass Classify(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return OriginAddressClass.Public;
        // A trailing root dot makes a name absolute without changing it (RFC 1034): "localhost." is localhost and
        // "127.0.0.1." is 127.0.0.1, so the host is classified without it.
        var rooted = uri.IdnHost;
        if (uri.HostNameType == UriHostNameType.Dns && rooted.Length > 1 && rooted.EndsWith('.')
            && Uri.TryCreate($"{uri.Scheme}://{rooted.TrimEnd('.')}", UriKind.Absolute, out var unrooted))
        {
            uri = unrooted;
        }
        if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            var host = uri.IdnHost;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                ? OriginAddressClass.Loopback
                : OriginAddressClass.Public;
        }

        if (!IPAddress.TryParse(uri.DnsSafeHost, out var address)) return OriginAddressClass.Public;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return OriginAddressClass.Unspecified;
        if (IPAddress.IsLoopback(address)) return OriginAddressClass.Loopback;
        foreach (var network in PrivateNetworks)
        {
            if (network.Contains(address)) return OriginAddressClass.Private;
        }
        return OriginAddressClass.Public;
    }

    private void Record(string origin)
    {
        var now = _time.GetUtcNow();
        if (_entries.TryGetValue(origin, out var entry))
        {
            entry.Hit(now);
            return;
        }

        ObservedOrigin? appeared = null;
        lock (_admitting)
        {
            if (_entries.TryGetValue(origin, out entry))
            {
                entry.Hit(now);
            }
            else
            {
                while (_entries.Count >= _limit) EvictLeastRecent();
                entry = new Entry(origin, Classify(origin), now);
                _entries[origin] = entry;
                appeared = entry.Snapshot();
            }
        }

        if (appeared is not null) Raise(appeared);
    }

    private void EvictLeastRecent()
    {
        Entry? oldest = null;
        foreach (var (_, entry) in _entries)
        {
            if (oldest is null || entry.LastTicks < oldest.LastTicks) oldest = entry;
        }
        if (oldest is not null) _entries.TryRemove(oldest.Origin, out _);
    }

    private void Raise(ObservedOrigin origin)
    {
        var handlers = Appeared;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<ObservedOrigin>>())
        {
            try
            {
                handler(this, origin);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception,
                    "A handler of {Event} failed for {Origin}; the request it arrived on continues.", nameof(Appeared), origin.Origin);
            }
        }
    }

    // The base URL in force, split once per value into what a request's scheme and Host header are compared with.
    private BaseOrigin? Base()
    {
        var current = _baseUrl.Current;
        if (current is null) return null;
        var cached = Volatile.Read(ref _base);
        if (cached is not null && ReferenceEquals(cached.Source, current)) return cached;

        var uri = new Uri(current);
        var fresh = new BaseOrigin(current, uri.GetLeftPart(UriPartial.Authority), uri.Scheme, uri.Authority);
        Volatile.Write(ref _base, fresh);
        return fresh;
    }

    private sealed record BaseOrigin(string Source, string Origin, string Scheme, string Authority);

    private sealed class Entry(string origin, OriginAddressClass addressClass, DateTimeOffset first)
    {
        private long _count = 1;
        private long _lastTicks = first.UtcTicks;

        public string Origin { get; } = origin;

        public long LastTicks => Volatile.Read(ref _lastTicks);

        public void Hit(DateTimeOffset now)
        {
            Interlocked.Increment(ref _count);
            var ticks = now.UtcTicks;
            long seen;
            do
            {
                seen = Volatile.Read(ref _lastTicks);
                if (ticks <= seen) return;
            }
            while (Interlocked.CompareExchange(ref _lastTicks, ticks, seen) != seen);
        }

        public ObservedOrigin Snapshot()
            => new(Origin, addressClass, Volatile.Read(ref _count), first, new DateTimeOffset(LastTicks, TimeSpan.Zero));
    }
}
