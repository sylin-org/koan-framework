namespace S5.Recs.Services;

/// <summary>
/// Cache service for storing and retrieving embeddings to avoid expensive AI calls.
/// </summary>
public interface IEmbeddingCache
{
    /// <summary>
    /// Retrieve a cached embedding by content hash and model ID.
    /// </summary>
    Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default);

    /// <summary>
    /// Store an embedding in the cache.
    /// </summary>
    Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default);

    /// <summary>
    /// Flush all cached embeddings.
    /// </summary>
    Task<int> FlushAsync(CancellationToken ct = default);

    /// <summary>
    /// Get cache statistics (total count, size, etc.).
    /// </summary>
    Task<CacheStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Bulk export all cached embeddings for a given model and entity type.
    /// Returns dictionary keyed by contentHash.
    /// </summary>
    Task<Dictionary<string, CachedEmbedding>> GetAllAsync(string modelId, string entityTypeName, CancellationToken ct = default);

    /// <summary>
    /// Stream cached embeddings in pages for incremental processing.
    /// Yields pages of (contentHash, embedding) dictionaries.
    /// </summary>
    IAsyncEnumerable<Dictionary<string, CachedEmbedding>> GetPaginatedAsync(string modelId, string entityTypeName, int pageSize = 1000, CancellationToken ct = default);
}

/// <summary>
/// Statistics about the embedding cache.
/// </summary>
public sealed class CacheStats
{
    public int TotalEmbeddings { get; init; }
    public long TotalSizeBytes { get; init; }
    public Dictionary<string, int> EmbeddingsByModel { get; init; } = new();
}
