namespace Koan.Media.Web.Caching;

/// <summary>
/// Opt-in cache for media transform output. Keyed by the source storage key plus a canonical
/// representation of the operator pipeline (operator ids + their normalized params), so the
/// same request (e.g. <c>?w=400&amp;h=400&amp;fit=cover&amp;format=webp</c>) always lands on the
/// same cache entry regardless of param order or alias choice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional</b>: when no implementation is registered in DI, <see cref="Controllers.MediaContentController{TEntity}"/>
/// transforms on every request. Browsers still cache via <c>ETag</c>/<c>Cache-Control</c> the
/// controller emits, so the cost of missing this is bounded.
/// </para>
/// <para>
/// <b>Implementation notes</b>: <see cref="WriteAsync"/> should be best-effort — implementations
/// must not throw on quota / IO errors; the controller swallows write failures so a flaky cache
/// can't break successful transforms.
/// </para>
/// </remarks>
public interface IMediaTransformCache
{
    Task<MediaCacheEntry?> TryGetAsync(string cacheKey, CancellationToken ct);
    Task WriteAsync(string cacheKey, MediaCacheEntry entry, CancellationToken ct);
}

/// <summary>Immutable cached transform output. <see cref="Bytes"/> is the encoded payload (e.g. webp/jpeg).</summary>
public sealed record MediaCacheEntry(byte[] Bytes, string ContentType, string ETag);
