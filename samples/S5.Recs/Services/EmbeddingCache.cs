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
    /// Stream cached embeddings in pages for incremental processing.
    /// Yields pages of (contentHash, embedding) pairs.
    /// </summary>
    public async IAsyncEnumerable<Dictionary<string, CachedEmbedding>> GetPaginatedAsync(
        string modelId,
        string entityTypeName,
        int pageSize = 1000,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var safeEntityType = SanitizeForFileSystem(entityTypeName);
        var safeModelId = SanitizeForFileSystem(modelId);
        var modelDir = Path.Combine(_basePath, safeEntityType, safeModelId);

        if (!Directory.Exists(modelDir))
        {
            yield break;
        }

        var files = Directory.GetFiles(modelDir, "*.json", SearchOption.AllDirectories);
        var totalFiles = files.Length;

        if (totalFiles == 0)
        {
            yield break;
        }

        _logger?.LogInformation("Streaming {Count} cached embeddings in pages of {PageSize}", totalFiles, pageSize);

        var startTime = DateTimeOffset.UtcNow;
        var processed = 0;

        for (int i = 0; i < totalFiles; i += pageSize)
        {
            var pageFiles = files.Skip(i).Take(pageSize).ToArray();
            var page = new Dictionary<string, CachedEmbedding>();

            // Parallel load within page
            var tasks = pageFiles.Select(async file =>
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, ct);
                    return JsonSerializer.Deserialize<CachedEmbedding>(json);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read cache file: {FilePath}", file);
                    return null;
                }
            });

            var pageResults = await Task.WhenAll(tasks);

            foreach (var cached in pageResults)
            {
                if (cached != null && !string.IsNullOrEmpty(cached.ContentHash))
                {
                    page[cached.ContentHash] = cached;
                }
            }

            processed += pageFiles.Length;

            // Calculate progress and ETA
            var elapsed = DateTimeOffset.UtcNow - startTime;
            var progressPercent = (double)processed / totalFiles * 100;
            var itemsPerSecond = processed / Math.Max(1, elapsed.TotalSeconds);
            var remaining = totalFiles - processed;
            var etaSeconds = remaining / Math.Max(0.1, itemsPerSecond);
            var eta = TimeSpan.FromSeconds(etaSeconds);

            _logger?.LogInformation("Cache streaming progress: {Processed}/{Total} files ({Percent:F1}%) - ETA: {ETA} - Yielding {PageCount} embeddings",
                processed, totalFiles, progressPercent, eta.ToString(@"mm\:ss"), page.Count);

            yield return page;
        }

        _logger?.LogInformation("Cache streaming complete: {Total} files in {Elapsed:F1}s", totalFiles, (DateTimeOffset.UtcNow - startTime).TotalSeconds);
    }

    /// <summary>
    /// Bulk export all cached embeddings for a given model and entity type with pagination and progress.
    /// Returns dictionary keyed by contentHash.
    /// </summary>
    public async Task<Dictionary<string, CachedEmbedding>> GetAllAsync(string modelId, string entityTypeName, CancellationToken ct = default)
    {
        var result = new Dictionary<string, CachedEmbedding>();
        var safeEntityType = SanitizeForFileSystem(entityTypeName);
        var safeModelId = SanitizeForFileSystem(modelId);
        var modelDir = Path.Combine(_basePath, safeEntityType, safeModelId);

        if (!Directory.Exists(modelDir))
        {
            return result;
        }

        try
        {
            var files = Directory.GetFiles(modelDir, "*.json", SearchOption.AllDirectories);
            var totalFiles = files.Length;

            if (totalFiles == 0)
            {
                return result;
            }

            _logger?.LogInformation("Bulk loading {Count} cached embeddings from {Path}", totalFiles, modelDir);

            const int pageSize = 1000;
            var startTime = DateTimeOffset.UtcNow;
            var processed = 0;

            for (int i = 0; i < totalFiles; i += pageSize)
            {
                var pageFiles = files.Skip(i).Take(pageSize).ToArray();

                // Parallel processing for much faster file I/O
                var tasks = pageFiles.Select(async file =>
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file, ct);
                        var cached = JsonSerializer.Deserialize<CachedEmbedding>(json);

                        if (cached != null && !string.IsNullOrEmpty(cached.ContentHash))
                        {
                            return cached;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to read cached embedding file: {FilePath}", file);
                    }
                    return null;
                });

                var pageResults = await Task.WhenAll(tasks);

                // Add to result dictionary (not thread-safe, so do sequentially)
                foreach (var cached in pageResults)
                {
                    if (cached != null && !string.IsNullOrEmpty(cached.ContentHash))
                    {
                        result[cached.ContentHash] = cached;
                    }
                }

                processed += pageFiles.Length;

                // Calculate progress and ETA
                var elapsed = DateTimeOffset.UtcNow - startTime;
                var progressPercent = (double)processed / totalFiles * 100;
                var itemsPerSecond = processed / Math.Max(1, elapsed.TotalSeconds);
                var remaining = totalFiles - processed;
                var etaSeconds = remaining / Math.Max(0.1, itemsPerSecond);
                var eta = TimeSpan.FromSeconds(etaSeconds);

                _logger?.LogInformation("Cache loading progress: {Processed}/{Total} files ({Percent:F1}%) - ETA: {ETA}",
                    processed, totalFiles, progressPercent, eta.ToString(@"mm\:ss"));
            }

            _logger?.LogInformation("Bulk loaded {Count} cached embeddings in {Elapsed:F1}s", result.Count, (DateTimeOffset.UtcNow - startTime).TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to bulk load cached embeddings");
        }

        return result;
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
