using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Caching;

/// <summary>
/// <para><strong>Obsolete — see MEDIA-0007.</strong> The controller now no-ops
/// natively when the registered <see cref="Routing.IMediaSource"/> does not
/// persist derivations and no legacy cache is configured.</para>
///
/// <para>No-op cache used when <c>Koan:Media:Web:OutputCache</c> is disabled. Every
/// read misses and every write is dropped, so the controller renders exactly as
/// it did before the feature existed.</para>
/// </summary>
[Obsolete("Derivations now live in storage via IMediaSource.TryStoreDerivationAsync; see MEDIA-0007. Removed in MEDIA-0008.", error: false)]
internal sealed class NullMediaOutputCache : IMediaOutputCache
{
    public static readonly NullMediaOutputCache Instance = new();

    private NullMediaOutputCache() { }

    public Task<MediaCacheHit?> TryGetAsync(string id, string fingerprint, CancellationToken ct = default)
        => Task.FromResult<MediaCacheHit?>(null);

    public Task SetAsync(string id, string fingerprint, MediaOutput output, CancellationToken ct = default)
        => Task.CompletedTask;
}
