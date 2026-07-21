using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Core.Concurrency;

/// <summary>
/// DI registration helpers for the cross-cutting keyed lease-gate primitive.
/// </summary>
public static class KeyedLeaseGateServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IKeyedLeaseGate"/> as a singleton. Idempotent; safe to call from
    /// multiple pillars (cache, AI, file-system layers) that depend on per-key serialization.
    /// </summary>
    public static IServiceCollection AddKoanKeyedLeaseGate(this IServiceCollection services)
    {
        services.TryAddSingleton<IKeyedLeaseGate, KeyedLeaseGate>();
        return services;
    }
}
