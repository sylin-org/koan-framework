using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Microsoft.Extensions.Logging;
using S9.Location.Core.Diagnostics;
using S9.Location.Core.Models;

namespace S9.Location.Core.Services;

public sealed record LocationMetrics(int CanonicalCount, int CacheCount, int ParkedCount, DateTimeOffset GeneratedAt);

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
        var canonicalCount = await CanonicalLocation.Count(ct);
        var cacheCount = await ResolutionCache.Count(ct);
        var parkedCount = 0;

        using (DataSetContext.With(FlowSets.StageShort(FlowSets.Parked)))
        {
            parkedCount = await ParkedRecord<RawLocation>.Count(ct);
        }

        var snapshot = new LocationMetrics(canonicalCount, cacheCount, parkedCount, DateTimeOffset.UtcNow);
        _logger.LogDebug("Generated location metrics {@Metrics}", snapshot);
        return snapshot;
    }
}
