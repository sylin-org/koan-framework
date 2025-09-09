using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S8.Location.Core.Models;
using S8.Location.Core.Options;
using S8.Location.Core.Services;
using Sora.Core;
using Sora.Core.Observability.Health;
using Sora.Data.Core;

namespace S8.Location.Core.Health;

public class LocationHealthContributor : IHealthContributor
{
    public string Name => "S8.Location";
    public bool IsCritical => false;
    
    private readonly IAddressResolutionService _resolver;
    private readonly LocationOptions _options;
    private readonly ILogger<LocationHealthContributor> _logger;

    public LocationHealthContributor(
        IAddressResolutionService resolver,
        IOptions<LocationOptions> options,
        ILogger<LocationHealthContributor> logger)
    {
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var checks = new Dictionary<string, object>();
        var issues = new List<string>();

        try
        {
            // Test cache availability
            var testHash = _resolver.ComputeSHA512("health-check-test");
            await Data<ResolutionCache, string>.GetAsync(testHash, ct);
            checks["cache"] = "available";
        }
        catch (Exception ex)
        {
            checks["cache"] = "unavailable";
            issues.Add($"Cache unavailable: {ex.Message}");
        }

        // Check resolution service
        try
        {
            var normalized = _resolver.NormalizeAddress("123 Test St");
            checks["normalization"] = !string.IsNullOrEmpty(normalized) ? "working" : "failed";
        }
        catch (Exception ex)
        {
            checks["normalization"] = "failed";
            issues.Add($"Address normalization failed: {ex.Message}");
        }

        // Get cache statistics
        try
        {
            var allCacheEntries = await Data<ResolutionCache, string>.All(ct);
            checks["cache_entries"] = allCacheEntries.Count;
            
            // Calculate recent entries
            var recentCacheEntries = allCacheEntries.Count(c => c.ResolvedAt >= DateTime.UtcNow.AddHours(-24));
            checks["recent_cache_entries"] = recentCacheEntries;
        }
        catch (Exception ex)
        {
            issues.Add($"Cache statistics unavailable: {ex.Message}");
        }

        // Check configuration
        checks["cache_enabled"] = _options.Resolution.CacheEnabled;
        checks["google_maps_configured"] = !string.IsNullOrEmpty(_options.Geocoding.GoogleMapsApiKey);

        var state = issues.Count == 0 ? HealthState.Healthy : HealthState.Degraded;
        var message = issues.Count == 0 ? "All location services operational" : string.Join("; ", issues);

        return new HealthReport(Name, state, message, null, checks);
    }
}