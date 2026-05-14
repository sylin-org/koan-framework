using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Koan.Media.Web.Caching;

/// <summary>
/// LRU in-memory cache for transform output backed by <see cref="IMemoryCache"/>. Honors
/// <see cref="MediaTransformCacheOptions.SizeLimitBytes"/> via per-entry <c>Size</c> values, so
/// the cache self-evicts past the configured budget.
/// </summary>
/// <remarks>
/// Default implementation registered by <c>services.AddMediaTransformCache(...)</c>. Apps with
/// shared infrastructure (Redis, CDN-backed storage) can replace it by registering their own
/// <see cref="IMediaTransformCache"/> singleton before the registrar runs.
/// </remarks>
internal sealed class InMemoryMediaTransformCache : IMediaTransformCache, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly MediaTransformCacheOptions _options;

    public InMemoryMediaTransformCache(IOptions<MediaTransformCacheOptions> options)
    {
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.SizeLimitBytes,
        });
    }

    public Task<MediaCacheEntry?> TryGetAsync(string cacheKey, CancellationToken ct)
    {
        if (_cache.TryGetValue<MediaCacheEntry>(cacheKey, out var entry) && entry is not null)
        {
            return Task.FromResult<MediaCacheEntry?>(entry);
        }
        return Task.FromResult<MediaCacheEntry?>(null);
    }

    public Task WriteAsync(string cacheKey, MediaCacheEntry entry, CancellationToken ct)
    {
        // Per-entry cap: huge transforms (e.g. someone asking for a 4k JPEG) are written through
        // without caching so they don't blow the LRU budget. The next identical request just
        // re-encodes — slow but bounded.
        if (entry.Bytes.LongLength > _options.MaxEntryBytes) return Task.CompletedTask;

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            Size = entry.Bytes.LongLength,
        };
        if (_options.SlidingExpiration is { } sliding)
        {
            cacheEntryOptions.SlidingExpiration = sliding;
        }
        if (_options.AbsoluteExpiration is { } abs)
        {
            cacheEntryOptions.AbsoluteExpirationRelativeToNow = abs;
        }
        try
        {
            _cache.Set(cacheKey, entry, cacheEntryOptions);
        }
        catch
        {
            // Best-effort: a failure here must not break the successful transform response.
        }
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
