using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Services;

/// <summary>
/// File-based embedding cache using SHA-256 content hashing.
/// Cache structure: cache/embeddings/{entityType}/{modelId}/{hash}.json
/// </summary>
public sealed class EmbeddingCache : IEmbeddingCache
{
    private readonly ILogger<EmbeddingCache> _logger;
    private const string CacheBasePath = "cache/embeddings";

    public EmbeddingCache(ILogger<EmbeddingCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the content for use as a cache key.
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<CachedEmbedding?> GetAsync(
        string contentHash,
        string modelId,
        string entityTypeName,
        CancellationToken ct = default)
    {
        var cachePath = GetCachePath(entityTypeName, modelId, contentHash);

        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cachePath, ct);
            var cached = JsonSerializer.Deserialize<CachedEmbedding>(json);

            if (cached != null)
            {
                _logger.LogTrace("Cache HIT: {ContentHash} ({EntityType}/{ModelId})",
                    contentHash[..12], entityTypeName, modelId);
            }

            return cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cached embedding from {Path}", cachePath);
            return null;
        }
    }

    public async Task SetAsync(
        string contentHash,
        string modelId,
        float[] embedding,
        string entityTypeName,
        CancellationToken ct = default)
    {
        var cachePath = GetCachePath(entityTypeName, modelId, contentHash);
        var cacheDir = Path.GetDirectoryName(cachePath);

        if (!string.IsNullOrEmpty(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        var cached = new CachedEmbedding
        {
            ContentHash = contentHash,
            ModelId = modelId,
            Embedding = embedding,
            Dimension = embedding.Length,
            CachedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var json = JsonSerializer.Serialize(cached, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            await File.WriteAllTextAsync(cachePath, json, ct);

            _logger.LogTrace("Cache SET: {ContentHash} ({EntityType}/{ModelId}, {Dimension}d)",
                contentHash[..12], entityTypeName, modelId, embedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write cached embedding to {Path}", cachePath);
        }
    }

    public Task<int> FlushAsync(CancellationToken ct = default)
    {
        // No in-memory cache to flush; all writes are immediate
        return Task.FromResult(0);
    }

    public async Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(CacheBasePath))
        {
            return new CacheStats(0, 0, null, null);
        }

        var files = Directory.GetFiles(CacheBasePath, "*.json", SearchOption.AllDirectories);
        var totalSize = files.Sum(f => new FileInfo(f).Length);
        var totalEntries = files.Length;

        DateTimeOffset? oldest = null;
        DateTimeOffset? newest = null;

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var cached = JsonSerializer.Deserialize<CachedEmbedding>(json);

                if (cached != null)
                {
                    if (oldest == null || cached.CachedAt < oldest)
                        oldest = cached.CachedAt;

                    if (newest == null || cached.CachedAt > newest)
                        newest = cached.CachedAt;
                }
            }
            catch
            {
                // Ignore malformed cache files
            }
        }

        return new CacheStats(totalEntries, totalSize, oldest, newest);
    }

    private static string GetCachePath(string entityTypeName, string modelId, string contentHash)
    {
        // Sanitize modelId for filesystem (e.g., "granite3.3:8b" → "granite3.3_8b")
        var sanitizedModelId = modelId.Replace(":", "_").Replace("/", "_");

        return Path.Combine(
            CacheBasePath,
            entityTypeName,
            sanitizedModelId,
            $"{contentHash}.json");
    }
}
