using System.Collections.Concurrent;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5/D8 — a minimal in-process fixed-window rate limiter for the open, unauthenticated endpoints
/// (dynamic registration, device-code polling/verification). Per-key counters reset each window; a single DI
/// singleton serves all callers. Not a distributed limiter — a per-node bound is sufficient defence against an
/// open-endpoint flood, and it is contention-free (no shared write on the hot request path beyond one bucket).
/// </summary>
public sealed class FixedWindowRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _buckets = new(StringComparer.Ordinal);

    /// <summary>True if the call is within <paramref name="limit"/> for the current <paramref name="window"/>; false if it should be throttled.</summary>
    public bool TryAcquire(string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (limit <= 0) return true;
        var bucket = _buckets.AddOrUpdate(
            key,
            _ => new Window(now, 1),
            (_, existing) => now - existing.Start >= window ? new Window(now, 1) : existing with { Count = existing.Count + 1 });
        return bucket.Count <= limit;
    }

    private readonly record struct Window(DateTimeOffset Start, int Count);
}
