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

    /// <summary>
    /// Persistent render-output cache. Disabled by default; set
    /// <c>Koan:Media:Web:OutputCache:Enabled</c> + <c>:Path</c> to memoize
    /// rendered variants on disk and skip the pipeline on repeat requests.
    ///
    /// <para><strong>Obsolete — see MEDIA-0007.</strong> Derivations now live in
    /// storage via <c>IMediaSource.TryStoreDerivationAsync</c>; this block is
    /// retained for one release while hosts migrate. Removed in MEDIA-0008.</para>
    /// </summary>
    [Obsolete("Use IMediaSource.TryStoreDerivationAsync; see MEDIA-0007. Removed in MEDIA-0008.", error: false)]
#pragma warning disable CS0618
    public MediaOutputCacheOptions OutputCache { get; set; } = new();
#pragma warning restore CS0618

    /// <summary>
    /// Configuration for the <see cref="Sweep.MediaDerivationSweepService"/>.
    /// Disabled by default; enable to schedule orphan-derivation cleanup.
    /// </summary>
    public MediaDerivationSweepOptions DerivationSweep { get; set; } = new();
}
