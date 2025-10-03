using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace S5.Recs.Services;

/// <summary>
/// File-based implementation of embedding cache.
/// Stores embeddings in .Koan/cache/embeddings/{entityTypeName}/{modelId}/{contentHash}.json
/// </summary>
public sealed class EmbeddingCache : IEmbeddingCache
{
    private readonly string _basePath;
    private readonly ILogger<EmbeddingCache>? _logger;

    public EmbeddingCache(ILogger<EmbeddingCache>? logger = null)
    {
        _basePath = Path.Combine("cache", "embeddings");
        _logger = logger;
    }

    public async Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default)
    {
        var filePath = GetCacheFilePath(entityTypeName, modelId, contentHash);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var cached = JsonSerializer.Deserialize<CachedEmbedding>(json);

            if (cached == null)
            {
                _logger?.LogWarning("Failed to deserialize cached embedding: {FilePath}", filePath);
                return null;
            }

            return cached;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read cached embedding: {FilePath}", filePath);
            return null;
        }
    }

    public async Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default)
    {
        var cached = new CachedEmbedding
        {
            ContentHash = contentHash,
            ModelId = modelId,
            Embedding = embedding,
            Dimension = embedding.Length,
            CachedAt = DateTimeOffset.UtcNow
        };

        var filePath = GetCacheFilePath(entityTypeName, modelId, contentHash);
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var json = JsonSerializer.Serialize(cached, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write cached embedding: {FilePath}", filePath);
        }
    }

    public Task<int> FlushAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
        {
            return Task.FromResult(0);
        }

        var count = 0;
        try
        {
            var files = Directory.GetFiles(_basePath, "*.json", SearchOption.AllDirectories);
            count = files.Length;

            foreach (var file in files)
            {
                File.Delete(file);
            }

            // Clean up empty directories
            var directories = Directory.GetDirectories(_basePath, "*", SearchOption.AllDirectories);
            foreach (var dir in directories.OrderByDescending(d => d.Length)) // Delete deepest first
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }

            _logger?.LogInformation("Flushed {Count} cached embeddings", count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to flush embedding cache");
        }

        return Task.FromResult(count);
    }

    public Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        var stats = new CacheStats();

        if (!Directory.Exists(_basePath))
        {
            return Task.FromResult(stats);
        }

        try
        {
            var files = Directory.GetFiles(_basePath, "*.json", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => new FileInfo(f).Length);
            var byModel = new Dictionary<string, int>();

            foreach (var file in files)
            {
                var modelDir = Path.GetFileName(Path.GetDirectoryName(file));
                if (!string.IsNullOrEmpty(modelDir))
                {
                    byModel[modelDir] = byModel.GetValueOrDefault(modelDir) + 1;
                }
            }

            return Task.FromResult(new CacheStats
            {
                TotalEmbeddings = files.Length,
                TotalSizeBytes = totalSize,
                EmbeddingsByModel = byModel
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get embedding cache stats");
            return Task.FromResult(stats);
        }
    }

    /// <summary>
    /// Compute SHA256 hash of content for cache key.
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetCacheFilePath(string entityTypeName, string modelId, string contentHash)
    {
        // Sanitize entity type and model ID for file system
        var safeEntityType = SanitizeForFileSystem(entityTypeName);
        var safeModelId = SanitizeForFileSystem(modelId);
        return Path.Combine(_basePath, safeEntityType, safeModelId, $"{contentHash}.json");
    }

    private static string SanitizeForFileSystem(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized;
    }
}
