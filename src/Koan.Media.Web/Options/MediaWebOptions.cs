namespace Koan.Media.Web.Options;

/// <summary>
/// Options bound from <c>Koan:Media:Web</c>. Per MEDIA-0004 §13/§14.
/// </summary>
public sealed class MediaWebOptions
{
    public const string SectionPath = "Koan:Media:Web";

    /// <summary>Hard cap on the longest output edge (pixels). Default 4096.</summary>
    public int MaxOutputEdge { get; set; } = 4096;

    /// <summary>
    /// When true, unknown query params return 400. When false (default),
    /// they're surfaced via <c>X-Koan-Media-IgnoredParams</c>.
    /// </summary>
    public bool StrictUnknownParams { get; set; } = false;

    /// <summary>
    /// When true (default), allows ad-hoc URLs (no recipe seed). Set
    /// false in production to require a named recipe for every request.
    /// </summary>
    public bool AllowAdHoc { get; set; } = true;

    /// <summary>Route prefix. Default <c>/media</c>.</summary>
    public string RoutePrefix { get; set; } = "/media";

    /// <summary>
    /// Default cache-control header for variant responses (no
    /// content-addressable hash in the URL). Default
    /// <c>"public, max-age=3600, stale-while-revalidate=86400"</c>.
    /// </summary>
    public string DefaultCacheControl { get; set; } = "public, max-age=3600, stale-while-revalidate=86400";

    /// <summary>
    /// Cache-control header when the URL carries a content-addressable
    /// hash (<c>/media/{id}@{shortHash}/...</c>). Default immutable.
    /// </summary>
    public string ImmutableCacheControl { get; set; } = "public, immutable, max-age=31536000";
}
