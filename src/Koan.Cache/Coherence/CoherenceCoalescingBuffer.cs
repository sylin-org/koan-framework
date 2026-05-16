using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Coherence;

/// <summary>
/// Optional per-key debounce buffer for coherence broadcasts. Configured via
/// <see cref="CacheOptions.CoherenceCoalescingMs"/>; 0 = disabled (immediate publish).
/// </summary>
/// <remarks>
/// <para>
/// When enabled, multiple <see cref="CacheInvalidationKind.EvictKey"/> broadcasts on the same
/// key within the window collapse to one published message. Tag-based and EvictAll messages
/// are NEVER coalesced (lower frequency, higher impact — coalescing them risks correctness).
/// </para>
/// <para>
/// Hard cap (<see cref="CacheOptions.CoherenceCoalescingMaxBuffered"/>) flushes early to bound memory under storm.
/// </para>
/// </remarks>
internal sealed class CoherenceCoalescingBuffer : IDisposable
{
    private readonly IOptionsMonitor<CacheOptions> _options;
    private readonly ILogger<CoherenceCoalescingBuffer> _logger;
    private readonly Func<CacheInvalidation, CancellationToken, ValueTask> _publish;
    private readonly ConcurrentDictionary<string, ScheduledEvict> _pending = new(StringComparer.Ordinal);
    private readonly Timer _flushTimer;
    private bool _disposed;

    public CoherenceCoalescingBuffer(
        IOptionsMonitor<CacheOptions> options,
        ILogger<CoherenceCoalescingBuffer> logger,
        Func<CacheInvalidation, CancellationToken, ValueTask> publish)
    {
        _options = options;
        _logger = logger;
        _publish = publish;
        // 50ms tick — small enough to honour short windows; large enough to avoid syscall storms.
        _flushTimer = new Timer(_ => Flush(forceAll: false), null, dueTime: 50, period: 50);
    }

    public ValueTask Enqueue(CacheInvalidation invalidation, CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var coalesceMs = opts.CoherenceCoalescingMs;

        // Tag/EvictAll messages never coalesce; pass through immediately.
        if (invalidation.Kind != CacheInvalidationKind.EvictKey || coalesceMs <= 0 || invalidation.Key is null)
            return _publish(invalidation, ct);

        var keyValue = invalidation.Key.Value.Value;

        // Hard cap: flush early to bound memory.
        if (_pending.Count >= opts.CoherenceCoalescingMaxBuffered)
        {
            _logger.LogDebug("Koan.Cache: coalescing buffer at cap ({Cap}); flushing.", opts.CoherenceCoalescingMaxBuffered);
            Flush(forceAll: true);
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(coalesceMs);
        _pending.AddOrUpdate(
            keyValue,
            _ => new ScheduledEvict(invalidation, deadline),
            (_, _) => new ScheduledEvict(invalidation, deadline));

        return ValueTask.CompletedTask;
    }

    private void Flush(bool forceAll)
    {
        if (_disposed) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _pending)
        {
            if (forceAll || kvp.Value.Deadline <= now)
            {
                if (_pending.TryRemove(kvp.Key, out var scheduled))
                {
                    try
                    {
                        _ = _publish(scheduled.Invalidation, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Koan.Cache: coalesced publish failed for {Key}", kvp.Key);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();
        Flush(forceAll: true);
    }

    private readonly record struct ScheduledEvict(CacheInvalidation Invalidation, DateTimeOffset Deadline);
}
