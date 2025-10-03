namespace S5.Recs.Services;

/// <summary>
/// Service for caching raw API responses before parsing.
/// Enables rebuild-from-cache and offline development scenarios.
/// </summary>
public interface IRawCacheService
{
    /// <summary>
    /// Write a raw page response to cache.
    /// </summary>
    Task WritePageAsync(string source, string mediaType, string jobId, int pageNum, string rawJson, CancellationToken ct = default);

    /// <summary>
    /// Read all cached pages for a specific job.
    /// </summary>
    IAsyncEnumerable<(int PageNum, string RawJson)> ReadPagesAsync(string source, string mediaType, string jobId, CancellationToken ct = default);

    /// <summary>
    /// List all available cache manifests.
    /// </summary>
    Task<List<CacheManifest>> ListCachesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a specific cache manifest.
    /// </summary>
    Task<CacheManifest?> GetManifestAsync(string source, string mediaType, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the most recent cache manifest for a source/mediaType combination (for incremental imports).
    /// </summary>
    Task<CacheManifest?> GetLatestManifestAsync(string source, string mediaType, CancellationToken ct = default);

    /// <summary>
    /// Write cache manifest metadata.
    /// </summary>
    Task WriteManifestAsync(string source, string mediaType, string jobId, CacheManifest manifest, CancellationToken ct = default);

    /// <summary>
    /// Delete all cached raw responses.
    /// </summary>
    Task<int> FlushAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Delete cached responses for a specific source/mediaType combination.
    /// </summary>
    Task<int> FlushAsync(string? source = null, string? mediaType = null, CancellationToken ct = default);
}

/// <summary>
/// Metadata for a cached import job.
/// </summary>
public record CacheManifest
{
    public required string JobId { get; init; }
    public required string Source { get; init; }
    public required string MediaType { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public required int TotalPages { get; init; }
    public required int TotalItems { get; init; }
    public string? ConfigSnapshot { get; init; } // Optional: capture provider config
}
