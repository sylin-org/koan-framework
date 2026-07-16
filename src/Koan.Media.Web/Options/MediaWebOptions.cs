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
    /// Hard cap on source megapixels per MEDIA-0004 §13. Default 100MP
    /// (= 10000x10000). Enforced via a header-only Image.Identify pass
    /// before the full decode allocates memory. Set 0 to disable.
    /// </summary>
    public int MaxSourceMegapixels { get; set; } = 100;

    /// <summary>
    /// Hard cap on source animation frame count per MEDIA-0004 §13.
    /// Default 600. Set 0 to disable.
    /// </summary>
    public int MaxFrameCount { get; set; } = 600;

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

    /// <summary>
    /// Default cache-control header for variant responses (no
    /// content-addressable hash in the URL). Default
    /// <c>"public, max-age=3600, stale-while-revalidate=86400"</c>.
    /// </summary>
    public string DefaultCacheControl { get; set; } = "public, max-age=3600, stale-while-revalidate=86400";

}
