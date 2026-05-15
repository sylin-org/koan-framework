using Koan.Media.Web.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Media.Web.Extensions;

/// <summary>
/// DI helpers for opting into the transform cache. Without these calls, the controller still
/// transforms on every request (browsers still cache via the controller's <c>ETag</c>/<c>Cache-Control</c>)
/// — the cache layer just trades RAM/disk for repeat-request CPU.
/// </summary>
/// <remarks>
/// <para>
/// <b>v0.7.0:</b> the default implementation now rides the <c>Koan.Cache</c> pillar instead of
/// a dedicated <c>IMemoryCache</c>. Cross-node sharing activates automatically when an L2
/// adapter (<c>Koan.Cache.Adapter.Redis</c>) is referenced — no extra wiring needed.
/// See <see cref="MediaTransformCacheOptions"/> for the migration trade-off (byte-size budget →
/// TTL eviction).
/// </para>
/// </remarks>
public static class MediaTransformCacheServiceCollectionExtensions
{
    /// <summary>
    /// Register the default pillar-backed transform cache with platform defaults
    /// (see <see cref="MediaTransformCacheOptions"/>).
    /// </summary>
    public static IServiceCollection AddMediaTransformCache(this IServiceCollection services)
    {
        services.AddOptions<MediaTransformCacheOptions>();
        services.TryAddSingleton<IMediaTransformCache, MediaTransformCache>();
        return services;
    }

    /// <summary>
    /// Register the pillar-backed transform cache with overrides applied to
    /// <see cref="MediaTransformCacheOptions"/> (expirations, per-entry cap, cache tag/prefix).
    /// </summary>
    public static IServiceCollection AddMediaTransformCache(
        this IServiceCollection services,
        Action<MediaTransformCacheOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<MediaTransformCacheOptions>().Configure(configure);
        services.TryAddSingleton<IMediaTransformCache, MediaTransformCache>();
        return services;
    }
}
