using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Media.Web.Caching;

namespace Koan.Media.Web.Extensions;

/// <summary>
/// DI helpers for opting into the transform cache. Without these calls, the controller still
/// transforms on every request (browsers still cache via the controller's <c>ETag</c>/<c>Cache-Control</c>)
/// — the cache layer just trades RAM/disk for repeat-request CPU.
/// </summary>
public static class MediaTransformCacheServiceCollectionExtensions
{
    /// <summary>
    /// Register the default in-memory transform cache with platform defaults
    /// (see <see cref="MediaTransformCacheOptions"/>).
    /// </summary>
    public static IServiceCollection AddMediaTransformCache(this IServiceCollection services)
    {
        services.AddOptions<MediaTransformCacheOptions>();
        services.TryAddSingleton<IMediaTransformCache, InMemoryMediaTransformCache>();
        return services;
    }

    /// <summary>
    /// Register the in-memory transform cache with overrides applied to
    /// <see cref="MediaTransformCacheOptions"/> (size budget, expirations, per-entry cap).
    /// </summary>
    public static IServiceCollection AddMediaTransformCache(
        this IServiceCollection services,
        Action<MediaTransformCacheOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<MediaTransformCacheOptions>().Configure(configure);
        services.TryAddSingleton<IMediaTransformCache, InMemoryMediaTransformCache>();
        return services;
    }
}
