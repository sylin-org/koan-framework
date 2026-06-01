namespace Koan.Media.Web.Caching;

/// <summary>
/// <para><strong>Obsolete — see MEDIA-0007.</strong> Use
/// <see cref="Routing.MediaDerivationHandle"/> instead.</para>
///
/// <para>A cache hit returned by <see cref="IMediaOutputCache.TryGetAsync"/>. The
/// caller streams and disposes <see cref="Bytes"/>.</para>
/// </summary>
/// <param name="Bytes">Readable stream over the stored render output.</param>
/// <param name="ContentType">MIME type of the stored bytes.</param>
[Obsolete("Use Routing.MediaDerivationHandle; see MEDIA-0007. Removed in MEDIA-0008.", error: false)]
public sealed record MediaCacheHit(Stream Bytes, string ContentType);
