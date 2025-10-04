using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Microsoft.Extensions.Logging;
using S9.Location.Core.Diagnostics;
using S9.Location.Core.Models;

namespace S9.Location.Core.Services;

public sealed record LocationMetrics(long CanonicalCount, long CacheCount, long ParkedCount, DateTimeOffset GeneratedAt);

public interface ILocationMetricsService
{
    Task<LocationMetrics> GetSummaryAsync(CancellationToken ct = default);
}

public sealed class LocationMetricsService : ILocationMetricsService
{
    private readonly ILogger<LocationMetricsService> _logger;

    public LocationMetricsService(ILogger<LocationMetricsService> logger)
    {
        _logger = logger;
    }

    public async Task<LocationMetrics> GetSummaryAsync(CancellationToken ct = default)
    {
        var canonicalCount = await CanonicalLocation.Count;
        var cacheCount = await ResolutionCache.Count;
        var parkedCount = 0L;

        using (EntityContext.With(CanonSets.StageShort(CanonSets.Parked)))
        {
            parkedCount = await ParkedRecord<RawLocation>.Count;
        }

        var snapshot = new LocationMetrics(canonicalCount, cacheCount, parkedCount, DateTimeOffset.UtcNow);
        _logger.LogDebug("Generated location metrics {@Metrics}", snapshot);
        return snapshot;
    }
}
