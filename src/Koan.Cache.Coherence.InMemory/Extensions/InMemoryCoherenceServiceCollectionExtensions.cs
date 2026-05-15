using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Coherence.InMemory.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Cache.Coherence.InMemory.Extensions;

/// <summary>
/// DI registration for the in-process coherence channel.
/// </summary>
public static class InMemoryCoherenceServiceCollectionExtensions
{
    /// <summary>
    /// Register the in-process coherence channel. Idempotent. The <see cref="InMemoryCoherenceBus"/>
    /// is registered as a singleton; tests can override it by registering their own bus instance
    /// before calling this method.
    /// </summary>
    public static IServiceCollection AddKoanCacheInMemoryCoherence(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryCoherenceBus>();
        services.TryAddSingleton<InMemoryCoherenceChannel>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCoherenceChannel, InMemoryCoherenceChannel>(
                sp => sp.GetRequiredService<InMemoryCoherenceChannel>()));
        return services;
    }
}
