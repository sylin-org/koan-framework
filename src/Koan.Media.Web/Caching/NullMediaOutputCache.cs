using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Caching;

/// <summary>
/// No-op cache used when <c>Koan:Media:Web:OutputCache</c> is disabled. Every
/// read misses and every write is dropped, so the controller renders exactly as
/// it did before the feature existed.
/// </summary>
internal sealed class NullMediaOutputCache : IMediaOutputCache
{
    public static readonly NullMediaOutputCache Instance = new();

    private NullMediaOutputCache() { }

    public Task<MediaCacheHit?> TryGetAsync(string id, string fingerprint, CancellationToken ct = default)
        => Task.FromResult<MediaCacheHit?>(null);

    public Task SetAsync(string id, string fingerprint, MediaOutput output, CancellationToken ct = default)
        => Task.CompletedTask;
}
