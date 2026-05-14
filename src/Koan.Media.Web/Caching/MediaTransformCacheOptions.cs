namespace Koan.Media.Web.Caching;

/// <summary>
/// Configuration for the in-memory transform cache. Pass through
/// <c>services.AddMediaTransformCache(opts =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutability assumption</b>: by default, both <see cref="SlidingExpiration"/> and
/// <see cref="AbsoluteExpiration"/> are <c>null</c>. Koan's storage layer uses content-addressed
/// keys (SHA-256), so a transform output is a deterministic function of (key, params) — there's
/// nothing to "go stale". The cache is bounded purely by <see cref="SizeLimitBytes"/> via LRU
/// eviction.
/// </para>
/// <para>
/// Apps that route mutable storage keys through this cache (rare — the key would have to be a
/// human path like <c>users/me/avatar.jpg</c>) should set a sliding or absolute expiration to
/// bound how long stale renders survive.
/// </para>
/// </remarks>
public sealed class MediaTransformCacheOptions
{
    /// <summary>Maximum cache footprint in bytes; <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> evicts least-recently-used entries past this budget. Default 128 MiB.</summary>
    public long SizeLimitBytes { get; set; } = 128L * 1024L * 1024L;

    /// <summary>
    /// Per-entry inactivity window before eviction. <c>null</c> by default — see immutability
    /// note in the class doc. Set a value (e.g. <c>TimeSpan.FromHours(6)</c>) only if the source
    /// keys can mutate behind a stable URL.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Absolute max age before eviction regardless of access. <c>null</c> by default.
    /// </summary>
    public TimeSpan? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Soft per-entry size cap. Entries larger than this are not cached — saves the budget for
    /// thumbnails that get repeated hits over the rare full-size transform. Default 4 MiB.
    /// </summary>
    public long MaxEntryBytes { get; set; } = 4L * 1024L * 1024L;
}
