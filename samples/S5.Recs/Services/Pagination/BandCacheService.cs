using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using S5.Recs.Controllers;
using S5.Recs.Models;

namespace S5.Recs.Services.Pagination;

/// <summary>
/// Implementation of sliding window cache service for vector search pagination.
/// ADR-0052: Adaptive Sliding Window Pagination for Vector Search
/// </summary>
internal sealed class BandCacheService : IBandCacheService
{
    private readonly IServiceProvider _sp;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BandCacheService>? _logger;
    private readonly IRecsService _recsService;

    private long _cacheHits;
    private long _cacheMisses;
    private readonly object _statsLock = new();

    // Configuration
    private const int DEFAULT_BAND_SIZE = 500;  // Target items per band fetch
    private const int CACHE_WINDOW_SIZE = 2000;  // Max items per cache
    private const int PREFETCH_THRESHOLD = 300;  // Trigger prefetch at 300 items from edge
    private const double INITIAL_BAND_WIDTH = 0.1;  // Start with 10% score range
    private const int MAX_BAND_ATTEMPTS = 10;  // Max attempts to widen band

    public BandCacheService(
        IServiceProvider sp,
        IMemoryCache cache,
        IRecsService recsService,
        ILogger<BandCacheService>? logger = null)
    {
        _sp = sp;
        _cache = cache;
        _recsService = recsService;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<Recommendation> items, bool degraded)> GetPageAsync(
        RecsQuery query,
        int offset,
        int limit,
        string? userId,
        CancellationToken ct)
    {
        var queryHash = GenerateQueryHash(query, userId);
        var cacheKey = $"band_cache:{userId ?? "anon"}:{queryHash}";

        // Try to get existing cache
        if (!_cache.TryGetValue<SlidingWindowCache>(cacheKey, out var cache) || cache == null)
        {
            // Cache miss - initialize new cache
            _logger?.LogDebug("Cache miss for {CacheKey}, initializing", cacheKey);
            lock (_statsLock) { _cacheMisses++; }

            cache = await InitializeCacheAsync(query, userId, limit, ct);
            if (cache == null || cache.Count == 0)
            {
                return (Array.Empty<Recommendation>(), true);
            }

            cache.QueryHash = queryHash;

            // Store in cache with 10-minute sliding expiration
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(10))
                .SetSize(cache.Count);  // For memory limit enforcement

            _cache.Set(cacheKey, cache, cacheOptions);
        }
        else
        {
            lock (_statsLock) { _cacheHits++; }
        }

        // Update last accessed time
        cache.LastAccessed = DateTimeOffset.UtcNow;

        // Check if requested range is in cache
        if (!cache.ContainsRange(offset, limit))
        {
            // Requested offset is beyond cache - need to expand
            _logger?.LogWarning("Offset {Offset} beyond cache size {Size}, fetching more bands",
                offset, cache.Count);

            // Fetch additional bands to cover the offset
            await ExpandCacheToOffset(cache, query, userId, offset + limit, ct);
        }

        // Trigger prefetch if approaching edges
        if (cache.ShouldPrefetchLower(offset, limit))
        {
            _logger?.LogDebug("Triggering lower bound prefetch at offset {Offset}", offset);
            _ = Task.Run(async () => await PrefetchLowerBandAsync(cache, query, userId, cacheKey, ct));
        }

        if (cache.ShouldPrefetchUpper(offset))
        {
            _logger?.LogDebug("Triggering upper bound prefetch at offset {Offset}", offset);
            _ = Task.Run(async () => await PrefetchUpperBandAsync(cache, query, userId, cacheKey, ct));
        }

        // Return the requested page
        var page = cache.GetPage(offset, limit);

        _logger?.LogDebug("Returned page {Offset}-{End} from cache (cache size: {Size}, bounds: [{Upper:F2}, {Lower:F2}])",
            offset, offset + limit, cache.Count, cache.UpperScoreBound, cache.LowerScoreBound);

        return (page, false);
    }

    private async Task<SlidingWindowCache?> InitializeCacheAsync(
        RecsQuery query,
        string? userId,
        int pageSize,
        CancellationToken ct)
    {
        var cache = new SlidingWindowCache
        {
            WindowSize = CACHE_WINDOW_SIZE,
            PrefetchThreshold = PREFETCH_THRESHOLD
        };

        // Target: fetch 5 pages worth of items for initial buffer
        var targetSize = pageSize * 5;

        _logger?.LogInformation("Initializing cache with target {TargetSize} items", targetSize);

        // Strategy: For initialization, fetch a large pool WITHOUT score filtering
        // This ensures we capture all available results regardless of score distribution
        var initialQuery = query with
        {
            TopK = Math.Max(targetSize * 2, 1000),  // Fetch 2x target or 1000, whichever is larger
            Offset = null,
            Limit = null,
            ExcludeIds = null
        };

        var (initialResults, degraded) = await _recsService.QueryAsync(initialQuery, userId, ct);

        if (degraded)
        {
            _logger?.LogWarning("Initial cache fetch degraded to fallback mode");
        }

        var allItems = initialResults
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Media.Id, StringComparer.Ordinal)
            .Take(Math.Min(targetSize, CACHE_WINDOW_SIZE))  // Cap at window size
            .ToList();

        _logger?.LogInformation("Cache initialized with {Count} items from initial fetch (requested {Requested}, got {Total})",
            allItems.Count, initialQuery.TopK, initialResults.Count);

        // If we got fewer items than requested, try fetching more with score bands
        var attempts = 1;
        if (allItems.Count < targetSize && allItems.Count > 0)
        {
            _logger?.LogDebug("Initial fetch returned {Count} < {Target}, attempting band fetches", allItems.Count, targetSize);

            var lowerScoreBound = allItems[^1].Score;  // Lowest score from initial fetch
            var bandWidth = INITIAL_BAND_WIDTH;

            while (allItems.Count < targetSize && attempts < MAX_BAND_ATTEMPTS)
            {
                var upperBound = lowerScoreBound;
                var lowerBound = upperBound - bandWidth;

                _logger?.LogDebug("Fetching band [{Upper:F2}, {Lower:F2}] (attempt {Attempt}, collected {Count}/{Target})",
                    upperBound, lowerBound, attempts + 1, allItems.Count, targetSize);

                var bandItems = await FetchScoreBandAsync(
                    query,
                    userId,
                    lowerBound,
                    upperBound,
                    DEFAULT_BAND_SIZE,
                    ct);

                if (bandItems.Count == 0)
                {
                    _logger?.LogDebug("Band returned 0 items, stopping fetch attempts");
                    break;  // No more results available
                }

                allItems.AddRange(bandItems);
                lowerScoreBound = lowerBound;

                // Adaptive widening: if band was sparse, widen faster
                if (bandItems.Count < pageSize / 2)
                {
                    bandWidth *= 1.5;
                    _logger?.LogDebug("Sparse region detected, widening band to {Width:F3}", bandWidth);
                }

                attempts++;
            }
        }

        // If still underfilled, log warning
        if (allItems.Count < pageSize)
        {
            _logger?.LogWarning(
                "Could not fulfill page size {PageSize}. Only {Count} items found after {Attempts} attempts",
                pageSize, allItems.Count, attempts);
        }
        else
        {
            _logger?.LogInformation("Cache initialized with {Count} items after {Attempts} band fetches",
                allItems.Count, attempts);
        }

        // Populate cache (items are already sorted by ScoreAndPersonalize)
        foreach (var item in allItems)
        {
            cache.Items[(item.Score, item.Media.Id)] = item;
        }

        // Set bounds
        if (cache.Count > 0)
        {
            cache.UpperScoreBound = cache.Items.Keys.First().Score;
            cache.LowerScoreBound = cache.Items.Keys.Last().Score;
        }

        cache.BandFetchCount = attempts;

        return cache;
    }

    private async Task ExpandCacheToOffset(
        SlidingWindowCache cache,
        RecsQuery query,
        string? userId,
        int requiredSize,
        CancellationToken ct)
    {
        var bandWidth = INITIAL_BAND_WIDTH;
        var attempts = 0;

        while (cache.Count < requiredSize && attempts < MAX_BAND_ATTEMPTS)
        {
            var upperBound = cache.LowerScoreBound;
            var lowerBound = upperBound - bandWidth;

            var bandItems = await FetchScoreBandAsync(
                query,
                userId,
                lowerBound,
                upperBound,
                DEFAULT_BAND_SIZE,
                ct);

            // Merge into cache (evict top items to maintain window size)
            cache.MergeAndEvict(bandItems, evictTop: true);

            _logger?.LogDebug("Expanded cache with {Count} items (now {Total}, bounds: [{Upper:F2}, {Lower:F2}])",
                bandItems.Count, cache.Count, cache.UpperScoreBound, cache.LowerScoreBound);

            attempts++;
        }
    }

    private async Task PrefetchLowerBandAsync(
        SlidingWindowCache cache,
        RecsQuery query,
        string? userId,
        string cacheKey,
        CancellationToken ct)
    {
        // Prevent concurrent prefetches
        lock (cache)
        {
            if (cache.IsPrefetchingLower) return;
            cache.IsPrefetchingLower = true;
        }

        try
        {
            var upperBound = cache.LowerScoreBound;
            var lowerBound = upperBound - INITIAL_BAND_WIDTH;

            _logger?.LogDebug("Background prefetch: fetching lower band [{Upper:F2}, {Lower:F2}]",
                upperBound, lowerBound);

            var bandItems = await FetchScoreBandAsync(
                query,
                userId,
                lowerBound,
                upperBound,
                DEFAULT_BAND_SIZE,
                ct);

            if (bandItems.Count > 0)
            {
                // Merge into cache
                lock (cache)
                {
                    cache.MergeAndEvict(bandItems, evictTop: true);
                }

                _logger?.LogInformation("Prefetched lower band: {Count} items (cache now {Total}, bounds: [{Upper:F2}, {Lower:F2}])",
                    bandItems.Count, cache.Count, cache.UpperScoreBound, cache.LowerScoreBound);

                // Update cache in memory
                _cache.Set(cacheKey, cache, new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(10))
                    .SetSize(cache.Count));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to prefetch lower band");
        }
        finally
        {
            lock (cache)
            {
                cache.IsPrefetchingLower = false;
            }
        }
    }

    private async Task PrefetchUpperBandAsync(
        SlidingWindowCache cache,
        RecsQuery query,
        string? userId,
        string cacheKey,
        CancellationToken ct)
    {
        // Prevent concurrent prefetches
        lock (cache)
        {
            if (cache.IsPrefetchingUpper) return;
            cache.IsPrefetchingUpper = true;
        }

        try
        {
            var lowerBound = cache.UpperScoreBound;
            var upperBound = lowerBound + INITIAL_BAND_WIDTH;

            _logger?.LogDebug("Background prefetch: fetching upper band [{Upper:F2}, {Lower:F2}]",
                upperBound, lowerBound);

            var bandItems = await FetchScoreBandAsync(
                query,
                userId,
                lowerBound,
                upperBound,
                DEFAULT_BAND_SIZE,
                ct);

            if (bandItems.Count > 0)
            {
                // Merge into cache
                lock (cache)
                {
                    cache.MergeAndEvict(bandItems, evictTop: false);
                }

                _logger?.LogInformation("Prefetched upper band: {Count} items (cache now {Total}, bounds: [{Upper:F2}, {Lower:F2}])",
                    bandItems.Count, cache.Count, cache.UpperScoreBound, cache.LowerScoreBound);

                // Update cache in memory
                _cache.Set(cacheKey, cache, new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(10))
                    .SetSize(cache.Count));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to prefetch upper band");
        }
        finally
        {
            lock (cache)
            {
                cache.IsPrefetchingUpper = false;
            }
        }
    }

    private async Task<List<Recommendation>> FetchScoreBandAsync(
        RecsQuery query,
        string? userId,
        double scoreMin,
        double scoreMax,
        int targetSize,
        CancellationToken ct)
    {
        // Fetch a large pool from vector search (no limit on topK for bands)
        // We'll filter by score after personalization
        var poolSize = Math.Max(targetSize * 3, 1000);  // Over-fetch for filtering

        // Create a modified query without offset/limit (fetch large pool)
        var bandQuery = query with
        {
            TopK = poolSize,
            Offset = null,
            Limit = null,
            ExcludeIds = null  // Don't exclude any IDs for band fetches
        };

        // Fetch from RecsService (this applies vector search + personalization)
        var (results, degraded) = await _recsService.QueryAsync(bandQuery, userId, ct);

        if (degraded)
        {
            _logger?.LogWarning("Band fetch degraded to fallback mode");
        }

        // Filter by score band
        var bandFiltered = results
            .Where(r => r.Score >= scoreMin && r.Score < scoreMax)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Media.Id, StringComparer.Ordinal)  // Deterministic tie-breaking
            .Take(targetSize)
            .ToList();

        _logger?.LogDebug("Score band [{Min:F2}, {Max:F2}] filtered {Filtered} from {Pool} results",
            scoreMin, scoreMax, bandFiltered.Count, results.Count);

        return bandFiltered;
    }

    private static string GenerateQueryHash(RecsQuery query, string? userId)
    {
        // Hash query parameters to create unique cache key
        var hashInput = new
        {
            query.Text,
            query.AnchorMediaId,
            query.Sort,
            query.Alpha,
            UserId = userId,
            Filters = query.Filters != null ? new
            {
                query.Filters.Genres,
                query.Filters.EpisodesMax,
                query.Filters.SpoilerSafe,
                query.Filters.PreferTags,
                query.Filters.PreferWeight,
                query.Filters.MediaType,
                query.Filters.RatingMin,
                query.Filters.RatingMax,
                query.Filters.YearMin,
                query.Filters.YearMax,
                query.Filters.ShowCensored
            } : null
        };

        var json = JsonSerializer.Serialize(hashInput);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    public void InvalidateCache(string queryHash)
    {
        // Note: IMemoryCache doesn't have a direct way to enumerate keys
        // This is a placeholder - in production, consider using a dictionary to track keys
        _logger?.LogInformation("Cache invalidation requested for hash {Hash}", queryHash);
    }

    public void InvalidateAll()
    {
        // Compact the cache (removes expired entries)
        if (_cache is MemoryCache memCache)
        {
            memCache.Compact(1.0);
        }
        _logger?.LogInformation("All caches invalidated");
    }

    public (int TotalCaches, long TotalItems, double CacheHitRate) GetStatistics()
    {
        lock (_statsLock)
        {
            var totalRequests = _cacheHits + _cacheMisses;
            var hitRate = totalRequests > 0 ? (double)_cacheHits / totalRequests : 0;

            // Note: We can't easily count total caches/items with IMemoryCache
            // Would need custom tracking or using a different cache implementation
            return (0, 0, hitRate);
        }
    }
}
