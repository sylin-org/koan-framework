namespace Koan.Media.Web.Caching;

/// <summary>
/// A cache hit returned by <see cref="IMediaOutputCache.TryGetAsync"/>. The
/// caller streams and disposes <see cref="Bytes"/>.
/// </summary>
/// <param name="Bytes">Readable stream over the stored render output.</param>
/// <param name="ContentType">MIME type of the stored bytes.</param>
public sealed record MediaCacheHit(Stream Bytes, string ContentType);
