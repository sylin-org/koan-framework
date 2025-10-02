using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace S5.Recs.Services;

/// <summary>
/// File-based implementation of raw API response cache.
/// Storage: /app/cache/import-raw/{source}/{mediaType}/{jobId}/
/// </summary>
internal sealed class RawCacheService : IRawCacheService
{
    private readonly string _cacheRoot = "/app/cache/import-raw";
    private readonly ILogger<RawCacheService>? _logger;

    public RawCacheService(ILogger<RawCacheService>? logger = null)
    {
        _logger = logger;
        Directory.CreateDirectory(_cacheRoot);
    }

    public async Task WritePageAsync(string source, string mediaType, string jobId, int pageNum, string rawJson, CancellationToken ct = default)
    {
        var jobDir = GetJobDirectory(source, mediaType, jobId);
        Directory.CreateDirectory(jobDir);

        var pageFile = Path.Combine(jobDir, $"page-{pageNum:D5}.json");
        await File.WriteAllTextAsync(pageFile, rawJson, ct);

        _logger?.LogDebug("Cached raw page: {Source}/{MediaType}/{JobId}/page-{Page}",
            source, mediaType, jobId, pageNum);
    }

    public async IAsyncEnumerable<(int PageNum, string RawJson)> ReadPagesAsync(
        string source,
        string mediaType,
        string jobId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var jobDir = GetJobDirectory(source, mediaType, jobId);
        if (!Directory.Exists(jobDir))
        {
            _logger?.LogWarning("Cache directory not found: {JobDir}", jobDir);
            yield break;
        }

        var pageFiles = Directory.GetFiles(jobDir, "page-*.json")
            .OrderBy(f => f)
            .ToList();

        _logger?.LogInformation("Reading {Count} cached pages from {JobDir}", pageFiles.Count, jobDir);

        foreach (var pageFile in pageFiles)
        {
            if (ct.IsCancellationRequested) yield break;

            // Extract page number from filename: page-00001.json -> 1
            var fileName = Path.GetFileNameWithoutExtension(pageFile);
            var pageNumStr = fileName.Replace("page-", "");
            if (!int.TryParse(pageNumStr, out var pageNum))
            {
                _logger?.LogWarning("Invalid page file name: {FileName}", pageFile);
                continue;
            }

            var rawJson = await File.ReadAllTextAsync(pageFile, ct);
            yield return (pageNum, rawJson);
        }
    }

    public async Task<List<CacheManifest>> ListCachesAsync(CancellationToken ct = default)
    {
        var manifests = new List<CacheManifest>();

        if (!Directory.Exists(_cacheRoot))
            return manifests;

        foreach (var sourceDir in Directory.GetDirectories(_cacheRoot))
        {
            var source = Path.GetFileName(sourceDir);

            foreach (var mediaTypeDir in Directory.GetDirectories(sourceDir))
            {
                var mediaType = Path.GetFileName(mediaTypeDir);

                foreach (var jobDir in Directory.GetDirectories(mediaTypeDir))
                {
                    var jobId = Path.GetFileName(jobDir);
                    var manifestFile = Path.Combine(jobDir, "manifest.json");

                    if (File.Exists(manifestFile))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(manifestFile, ct);
                            var manifest = JsonConvert.DeserializeObject<CacheManifest>(json);
                            if (manifest != null)
                            {
                                manifests.Add(manifest);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to read manifest: {File}", manifestFile);
                        }
                    }
                }
            }
        }

        return manifests.OrderByDescending(m => m.FetchedAt).ToList();
    }

    public async Task<CacheManifest?> GetManifestAsync(string source, string mediaType, string jobId, CancellationToken ct = default)
    {
        var manifestFile = Path.Combine(GetJobDirectory(source, mediaType, jobId), "manifest.json");

        if (!File.Exists(manifestFile))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(manifestFile, ct);
            return JsonConvert.DeserializeObject<CacheManifest>(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read manifest: {File}", manifestFile);
            return null;
        }
    }

    public async Task<CacheManifest?> GetLatestManifestAsync(string source, string mediaType, CancellationToken ct = default)
    {
        var allManifests = await ListCachesAsync(ct);
        return allManifests
            .Where(m => m.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                       m.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.FetchedAt)
            .FirstOrDefault();
    }

    public async Task WriteManifestAsync(string source, string mediaType, string jobId, CacheManifest manifest, CancellationToken ct = default)
    {
        var jobDir = GetJobDirectory(source, mediaType, jobId);
        Directory.CreateDirectory(jobDir);

        var manifestFile = Path.Combine(jobDir, "manifest.json");
        var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        await File.WriteAllTextAsync(manifestFile, json, ct);

        _logger?.LogInformation("Wrote cache manifest: {Source}/{MediaType}/{JobId} ({Items} items, {Pages} pages)",
            source, mediaType, jobId, manifest.TotalItems, manifest.TotalPages);
    }

    public Task<int> FlushAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_cacheRoot))
            return Task.FromResult(0);

        var count = 0;
        foreach (var dir in Directory.GetDirectories(_cacheRoot))
        {
            Directory.Delete(dir, recursive: true);
            count++;
        }

        _logger?.LogInformation("Flushed all raw import cache ({Count} sources)", count);
        return Task.FromResult(count);
    }

    public Task<int> FlushAsync(string? source = null, string? mediaType = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_cacheRoot))
            return Task.FromResult(0);

        var count = 0;

        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(mediaType))
        {
            // Flush specific source/mediaType
            var mediaTypeDir = Path.Combine(_cacheRoot, source, mediaType);
            if (Directory.Exists(mediaTypeDir))
            {
                var jobDirs = Directory.GetDirectories(mediaTypeDir);
                count = jobDirs.Length;
                Directory.Delete(mediaTypeDir, recursive: true);
                _logger?.LogInformation("Flushed cache for {Source}/{MediaType} ({Count} jobs)", source, mediaType, count);
            }
        }
        else if (!string.IsNullOrWhiteSpace(source))
        {
            // Flush entire source
            var sourceDir = Path.Combine(_cacheRoot, source);
            if (Directory.Exists(sourceDir))
            {
                var mediaTypeDirs = Directory.GetDirectories(sourceDir);
                foreach (var dir in mediaTypeDirs)
                {
                    count += Directory.GetDirectories(dir).Length;
                }
                Directory.Delete(sourceDir, recursive: true);
                _logger?.LogInformation("Flushed cache for source {Source} ({Count} jobs)", source, count);
            }
        }
        else
        {
            // Flush all
            return FlushAllAsync(ct);
        }

        return Task.FromResult(count);
    }

    private string GetJobDirectory(string source, string mediaType, string jobId)
    {
        return Path.Combine(_cacheRoot, source, mediaType, jobId);
    }
}
