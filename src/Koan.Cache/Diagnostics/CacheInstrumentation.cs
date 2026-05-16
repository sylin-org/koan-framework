using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Diagnostics;

/// <summary>
/// OpenTelemetry-friendly instrumentation for the cache pillar — counters via
/// <see cref="Meter"/> ("Koan.Cache") and distributed-trace spans via <see cref="ActivitySource"/>
/// ("Koan.Cache"). Per-key verbose logging is gated by <see cref="CacheTraceFilter"/>.
/// </summary>
internal sealed class CacheInstrumentation : IDisposable
{
    /// <summary>Meter name. Subscribe via <c>OpenTelemetry.AddMeter("Koan.Cache")</c>.</summary>
    public const string MeterName = "Koan.Cache";

    /// <summary>ActivitySource name. Subscribe via <c>OpenTelemetry.AddSource("Koan.Cache")</c>.</summary>
    public const string ActivitySourceName = "Koan.Cache";

    private readonly ILogger<CacheInstrumentation> _logger;
    private readonly Meter _meter = new(MeterName, "0.7.0");

    // Aggregate counters
    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;
    private readonly Counter<long> _setCounter;
    private readonly Counter<long> _removeCounter;
    private readonly Counter<long> _invalidationCounter;

    // Coherence-side counters (M9 / bundle I)
    private readonly Counter<long> _coherencePublished;
    private readonly Counter<long> _coherenceReceived;
    private readonly Counter<long> _coherenceApplied;

    // Tier-side counters (M9)
    private readonly Counter<long> _tierFetches;
    private readonly Counter<long> _tierHits;
    private readonly Counter<long> _tierMisses;

    // Duration histograms (M9)
    private readonly Histogram<double> _readDurationMs;
    private readonly Histogram<double> _writeDurationMs;

    /// <summary>ActivitySource for distributed-trace spans.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "0.7.0");

    public CacheInstrumentation(ILogger<CacheInstrumentation> logger)
    {
        _logger = logger;
        _hitCounter = _meter.CreateCounter<long>("koan.cache.hits");
        _missCounter = _meter.CreateCounter<long>("koan.cache.misses");
        _setCounter = _meter.CreateCounter<long>("koan.cache.sets");
        _removeCounter = _meter.CreateCounter<long>("koan.cache.removes");
        _invalidationCounter = _meter.CreateCounter<long>("koan.cache.invalidations");

        _coherencePublished = _meter.CreateCounter<long>("koan.cache.coherence.published");
        _coherenceReceived = _meter.CreateCounter<long>("koan.cache.coherence.received");
        _coherenceApplied = _meter.CreateCounter<long>("koan.cache.coherence.applied");

        _tierFetches = _meter.CreateCounter<long>("koan.cache.tier.fetches");
        _tierHits = _meter.CreateCounter<long>("koan.cache.tier.hits");
        _tierMisses = _meter.CreateCounter<long>("koan.cache.tier.misses");

        _readDurationMs = _meter.CreateHistogram<double>("koan.cache.read.duration", "ms");
        _writeDurationMs = _meter.CreateHistogram<double>("koan.cache.write.duration", "ms");
    }

    public void RecordHit(string key, string provider)
    {
        _hitCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache hit for key {Key} on provider {Provider}.", key, provider);
        CacheTraceFilter.LogIfTraced(_logger, key, action: "hit", outcome: provider);
    }

    public void RecordMiss(string key, string provider)
    {
        _missCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache miss for key {Key} on provider {Provider}.", key, provider);
        CacheTraceFilter.LogIfTraced(_logger, key, action: "miss", outcome: provider);
    }

    public void RecordSet(string key, string provider)
    {
        _setCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogTrace("Cache set for key {Key} on provider {Provider}.", key, provider);
        CacheTraceFilter.LogIfTraced(_logger, key, action: "set", outcome: provider);
    }

    public void RecordRemove(string key, string provider, bool success)
    {
        _removeCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        if (success)
            _logger.LogTrace("Cache remove for key {Key} on provider {Provider} succeeded.", key, provider);
        else
            _logger.LogDebug("Cache remove for key {Key} on provider {Provider} found no entry.", key, provider);
        CacheTraceFilter.LogIfTraced(_logger, key, action: "remove", outcome: success ? "removed" : "missing");
    }

    public void RecordInvalidation(string key, string provider, string reason)
    {
        _invalidationCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache invalidation for key {Key} on provider {Provider} ({Reason}).", key, provider, reason);
        CacheTraceFilter.LogIfTraced(_logger, key, action: "invalidate", outcome: reason);
    }

    public void RecordCoherencePublished(string transport, string kind)
    {
        _coherencePublished.Add(1,
            new KeyValuePair<string, object?>("transport", transport),
            new KeyValuePair<string, object?>("kind", kind));
    }

    public void RecordCoherenceReceived(string transport, string kind)
    {
        _coherenceReceived.Add(1,
            new KeyValuePair<string, object?>("transport", transport),
            new KeyValuePair<string, object?>("kind", kind));
    }

    public void RecordCoherenceApplied(string transport, string kind)
    {
        _coherenceApplied.Add(1,
            new KeyValuePair<string, object?>("transport", transport),
            new KeyValuePair<string, object?>("kind", kind));
    }

    public void RecordTierFetch(string tier, bool hit)
    {
        _tierFetches.Add(1,
            new KeyValuePair<string, object?>("tier", tier),
            new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));
        if (hit)
            _tierHits.Add(1, new KeyValuePair<string, object?>("tier", tier));
        else
            _tierMisses.Add(1, new KeyValuePair<string, object?>("tier", tier));
    }

    public void RecordReadDuration(double milliseconds, bool hit)
        => _readDurationMs.Record(milliseconds, new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));

    public void RecordWriteDuration(double milliseconds)
        => _writeDurationMs.Record(milliseconds);

    /// <summary>
    /// Start an <see cref="ActivitySource"/> span for a cache operation. Returns null when no
    /// listener is subscribed (zero allocation in the common case).
    /// </summary>
    public static Activity? StartActivity(string operation, string key)
    {
        var activity = ActivitySource.StartActivity(operation, ActivityKind.Internal);
        activity?.SetTag("cache.key", key);
        return activity;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
