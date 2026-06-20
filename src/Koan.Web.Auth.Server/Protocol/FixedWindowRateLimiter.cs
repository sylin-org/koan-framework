using System.Collections.Concurrent;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5/D8 — a minimal in-process fixed-window rate limiter for the open, unauthenticated endpoints
/// (dynamic registration, device-code verification). A single DI singleton serves all callers. Per-key counters
/// reset each window; increments are atomic per key (a lock on the per-key counter), so concurrent requests for
/// the same key cannot over-admit. The key space is bounded by opportunistic pruning so an attacker rotating
/// source IPs cannot grow it without limit. Per-node (not distributed) — sufficient against an open-endpoint flood.
/// </summary>
public sealed class FixedWindowRateLimiter
{
    private const int PruneThreshold = 10_000;
    private readonly ConcurrentDictionary<string, Counter> _buckets = new(StringComparer.Ordinal);

    /// <summary>True if the call is within <paramref name="limit"/> for the current <paramref name="window"/>; false to throttle.</summary>
    public bool TryAcquire(string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (limit <= 0) return true;
        PruneIfLarge(now, window);

        var counter = _buckets.GetOrAdd(key, _ => new Counter(now));
        lock (counter)
        {
            if (now - counter.Start >= window) { counter.Start = now; counter.Count = 0; }
            counter.Count++;
            return counter.Count <= limit;
        }
    }

    private void PruneIfLarge(DateTimeOffset now, TimeSpan window)
    {
        if (_buckets.Count < PruneThreshold) return;
        foreach (var kvp in _buckets)
            if (now - kvp.Value.Start >= window)
                _buckets.TryRemove(kvp.Key, out _);
    }

    private sealed class Counter
    {
        public DateTimeOffset Start;
        public int Count;
        public Counter(DateTimeOffset start) => Start = start;
    }
}
