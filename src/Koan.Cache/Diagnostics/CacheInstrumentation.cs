using System;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Diagnostics;

internal sealed class CacheInstrumentation : IDisposable
{
    private readonly ILogger<CacheInstrumentation> _logger;
    private readonly Meter _meter = new("Koan.Cache", "0.6.3");
    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;
    private readonly Counter<long> _setCounter;
    private readonly Counter<long> _removeCounter;
    private readonly Counter<long> _invalidationCounter;

    public CacheInstrumentation(ILogger<CacheInstrumentation> logger)
    {
        _logger = logger;
        _hitCounter = _meter.CreateCounter<long>("koan.cache.hits");
        _missCounter = _meter.CreateCounter<long>("koan.cache.misses");
        _setCounter = _meter.CreateCounter<long>("koan.cache.sets");
        _removeCounter = _meter.CreateCounter<long>("koan.cache.removes");
        _invalidationCounter = _meter.CreateCounter<long>("koan.cache.invalidations");
    }

    public void RecordHit(string key, string provider)
    {
        _hitCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache hit for key {Key} on provider {Provider}.", key, provider);
    }

    public void RecordMiss(string key, string provider)
    {
        _missCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache miss for key {Key} on provider {Provider}.", key, provider);
    }

    public void RecordSet(string key, string provider)
    {
        _setCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogTrace("Cache set for key {Key} on provider {Provider}.", key, provider);
    }

    public void RecordRemove(string key, string provider, bool success)
    {
        _removeCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        if (success)
        {
            _logger.LogTrace("Cache remove for key {Key} on provider {Provider} succeeded.", key, provider);
        }
        else
        {
            _logger.LogWarning("Cache remove for key {Key} on provider {Provider} failed or key missing.", key, provider);
        }
    }

    public void RecordInvalidation(string key, string provider, string reason)
    {
        _invalidationCounter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _logger.LogDebug("Cache invalidation for key {Key} on provider {Provider} ({Reason}).", key, provider, reason);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
