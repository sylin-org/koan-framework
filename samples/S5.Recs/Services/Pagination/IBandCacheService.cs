using S5.Recs.Controllers;
using S5.Recs.Models;

namespace S5.Recs.Services.Pagination;

/// <summary>
/// Service for managing sliding window caches for vector search pagination.
/// ADR-0052: Adaptive Sliding Window Pagination for Vector Search
/// </summary>
public interface IBandCacheService
{
    /// <summary>
    /// Get recommendations for a specific page using sliding window cache.
    /// Automatically handles cache initialization, prefetching, and window maintenance.
    /// </summary>
    /// <param name="query">Recommendation query with filters, text, etc.</param>
    /// <param name="offset">Zero-based offset (e.g., 0 for page 1, 100 for page 2)</param>
    /// <param name="limit">Number of items per page</param>
    /// <param name="userId">User ID for personalization (null for anonymous)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of recommendations and degraded flag</returns>
    Task<(IReadOnlyList<Recommendation> items, bool degraded)> GetPageAsync(
        RecsQuery query,
        int offset,
        int limit,
        string? userId,
        CancellationToken ct);

    /// <summary>
    /// Invalidate cache for a specific query
    /// </summary>
    void InvalidateCache(string queryHash);

    /// <summary>
    /// Invalidate all caches (e.g., after bulk data update)
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Get cache statistics for observability
    /// </summary>
    (int TotalCaches, long TotalItems, double CacheHitRate) GetStatistics();
}
