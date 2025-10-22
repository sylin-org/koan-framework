using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Services;

/// <summary>
/// File-based embedding cache for reducing AI API calls.
/// </summary>
public interface IEmbeddingCache
{
    /// <summary>
    /// Retrieves a cached embedding by content hash and model ID.
    /// </summary>
    Task<CachedEmbedding?> GetAsync(
        string contentHash,
        string modelId,
        string entityTypeName,
        CancellationToken ct = default);

    /// <summary>
    /// Stores an embedding in the cache.
    /// </summary>
    Task SetAsync(
        string contentHash,
        string modelId,
        float[] embedding,
        string entityTypeName,
        CancellationToken ct = default);

    /// <summary>
    /// Flushes any in-memory cache to disk.
    /// </summary>
    Task<int> FlushAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves cache statistics.
    /// </summary>
    Task<CacheStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Cache statistics.
/// </summary>
public sealed record CacheStats(
    int TotalEntries,
    long TotalSizeBytes,
    DateTimeOffset? OldestEntry,
    DateTimeOffset? NewestEntry);
