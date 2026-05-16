using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Core.Singleflight;

/// <summary>
/// DI registration helpers for the cross-cutting singleflight primitive.
/// </summary>
public static class SingleflightServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="ISingleflightRegistry"/> as a singleton. Idempotent; safe to call
    /// from multiple pillars (cache, AI, file-system layers) that depend on stampede protection.
    /// </summary>
    public static IServiceCollection AddKoanSingleflight(this IServiceCollection services)
    {
        services.TryAddSingleton<ISingleflightRegistry, SingleflightRegistry>();
        return services;
    }
}
