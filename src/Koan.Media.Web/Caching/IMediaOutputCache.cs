using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Caching;

/// <summary>
/// <para><strong>Obsolete — see MEDIA-0007.</strong> Derivations are now persisted
/// through <see cref="Routing.IMediaSource.TryStoreDerivationAsync"/> as ordinary
/// storage entities under the same profile as their source. The cache abstraction
/// will be deleted in MEDIA-0008.</para>
///
/// <para>Historical: persistent cache for rendered recipe output. Consulted by
/// <see cref="Controllers.MediaController"/> before running the pipeline and
/// populated write-through after a render. The cache key is
/// <c>(id, fingerprint)</c> where the fingerprint is
/// <see cref="MediaRecipe.Fingerprint"/> of the effective (post-override)
/// recipe, already normalized, so equivalent requests share one entry.</para>
///
/// <para>Implementations must be resilient: a read failure should surface as a
/// miss (return null) and a write failure must never fault the response. The
/// pipeline is always able to reproduce the output, so the cache is strictly an
/// optimization.</para>
/// </summary>
[Obsolete("Derivations now live in storage via IMediaSource.TryStoreDerivationAsync; see MEDIA-0007. Removed in MEDIA-0008.", error: false)]
public interface IMediaOutputCache
{
    /// <summary>
    /// Return a stored render for <paramref name="id"/> + <paramref name="fingerprint"/>,
    /// or null on miss. The caller owns and disposes <see cref="MediaCacheHit.Bytes"/>.
    /// </summary>
    Task<MediaCacheHit?> TryGetAsync(string id, string fingerprint, CancellationToken ct = default);

    /// <summary>Persist <paramref name="output"/> under <paramref name="id"/> + <paramref name="fingerprint"/>.</summary>
    Task SetAsync(string id, string fingerprint, MediaOutput output, CancellationToken ct = default);
}
