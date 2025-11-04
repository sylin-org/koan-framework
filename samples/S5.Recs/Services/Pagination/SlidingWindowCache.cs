using S5.Recs.Models;

namespace S5.Recs.Services.Pagination;

/// <summary>
/// Sliding window cache for vector search results.
/// Maintains a sorted window of recommendations with adaptive score-band prefetching.
/// ADR-0052: Adaptive Sliding Window Pagination for Vector Search
/// </summary>
public sealed class SlidingWindowCache
{
    /// <summary>
    /// Sorted dictionary of recommendations by (Score DESC, Id ASC) for deterministic ordering.
    /// Using tuple key ensures stable sort even when scores are equal.
    /// </summary>
    public SortedList<(double Score, string Id), Recommendation> Items { get; set; } = new(
        Comparer<(double Score, string Id)>.Create((a, b) =>
        {
            // Sort by score descending (higher scores first)
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) return scoreCompare;

            // Tie-break by ID ascending (deterministic)
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        })
    );

    /// <summary>
    /// Upper bound of score range currently cached (highest score in cache)
    /// </summary>
    public double UpperScoreBound { get; set; } = 1.0;

    /// <summary>
    /// Lower bound of score range currently cached (lowest score in cache)
    /// </summary>
    public double LowerScoreBound { get; set; } = 0.9;

    /// <summary>
    /// Maximum number of items to keep in cache (evict beyond this)
    /// Default: 2000 items × 2KB ≈ 4MB per user
    /// </summary>
    public int WindowSize { get; set; } = 2000;

    /// <summary>
    /// Distance from edge to trigger prefetch (items, not offset)
    /// When user is within 300 items of edge, trigger background fetch
    /// </summary>
    public int PrefetchThreshold { get; set; } = 300;

    /// <summary>
    /// Last time this cache was accessed (for LRU eviction)
    /// </summary>
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Hash of query parameters (text, filters, userId, sort, etc.)
    /// Used to identify unique queries and invalidate cache on param changes
    /// </summary>
    public string QueryHash { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if a background prefetch is currently in progress
    /// </summary>
    public bool IsPrefetchingLower { get; set; }

    /// <summary>
    /// Indicates if a background prefetch for upper bound is in progress
    /// </summary>
    public bool IsPrefetchingUpper { get; set; }

    /// <summary>
    /// Total number of band fetches performed for this cache (metrics)
    /// </summary>
    public int BandFetchCount { get; set; }

    /// <summary>
    /// Get current cache size in items
    /// </summary>
    public int Count => Items.Count;

    /// <summary>
    /// Check if cache contains items in the requested offset range
    /// </summary>
    public bool ContainsRange(int offset, int limit)
    {
        return offset >= 0 && offset + limit <= Items.Count;
    }

    /// <summary>
    /// Get recommendations for a specific page (offset/limit)
    /// </summary>
    public List<Recommendation> GetPage(int offset, int limit)
    {
        return Items.Values
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Calculate distance from current position to lower edge
    /// </summary>
    public int DistanceFromLowerEdge(int offset, int limit)
    {
        var currentPosition = offset + (limit / 2);
        return Items.Count - currentPosition;
    }

    /// <summary>
    /// Calculate distance from current position to upper edge
    /// </summary>
    public int DistanceFromUpperEdge(int offset)
    {
        return offset;
    }

    /// <summary>
    /// Check if prefetch should be triggered for lower bound
    /// </summary>
    public bool ShouldPrefetchLower(int offset, int limit)
    {
        return !IsPrefetchingLower && DistanceFromLowerEdge(offset, limit) < PrefetchThreshold;
    }

    /// <summary>
    /// Check if prefetch should be triggered for upper bound
    /// </summary>
    public bool ShouldPrefetchUpper(int offset)
    {
        return !IsPrefetchingUpper && DistanceFromUpperEdge(offset) < PrefetchThreshold && offset > 0;
    }

    /// <summary>
    /// Merge new items into cache and maintain window size
    /// </summary>
    public void MergeAndEvict(List<Recommendation> newItems, bool evictTop)
    {
        // Add new items
        foreach (var item in newItems)
        {
            Items[(item.Score, item.Media.Id)] = item;
        }

        // Update bounds
        if (Items.Count > 0)
        {
            UpperScoreBound = Items.Keys.First().Score;
            LowerScoreBound = Items.Keys.Last().Score;
        }

        // Evict items beyond window size
        if (evictTop)
        {
            // Evict highest scores (when fetching lower bands)
            while (Items.Count > WindowSize)
            {
                Items.RemoveAt(0);
            }

            // Update upper bound after eviction
            if (Items.Count > 0)
            {
                UpperScoreBound = Items.Keys.First().Score;
            }
        }
        else
        {
            // Evict lowest scores (when fetching upper bands)
            while (Items.Count > WindowSize)
            {
                Items.RemoveAt(Items.Count - 1);
            }

            // Update lower bound after eviction
            if (Items.Count > 0)
            {
                LowerScoreBound = Items.Keys.Last().Score;
            }
        }

        BandFetchCount++;
    }
}
