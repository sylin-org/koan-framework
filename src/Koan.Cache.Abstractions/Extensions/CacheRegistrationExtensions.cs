using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Cache.Abstractions.Extensions;

/// <summary>
/// Typed registration helpers for cache adapter authors. Hide the descriptor shape so the
/// indistinguishable-descriptor bug class (commit 14a5e8ce) can't return.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> writing
/// <code>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ICacheStore&gt;(sp =&gt; ...))</code>
/// directly produces a descriptor whose <c>ImplementationType</c> equals the service type,
/// which <c>TryAddEnumerable</c> rejects as "indistinguishable" — the first call throws and
/// the host fails to boot. The correct idiom is the two-generic form
/// <code>ServiceDescriptor.Singleton&lt;TService, TImplementation&gt;(sp =&gt; ...)</code>.
/// These helpers bake the correct idiom in so adapter authors can't get it wrong.
/// </para>
/// <para>
/// <b>Pattern:</b> each helper registers the concrete type as a singleton (so direct
/// resolution returns the same instance the enumerable does), then appends the interface
/// projection via <c>TryAddEnumerable</c> with the distinguishable descriptor.
/// </para>
/// </remarks>
public static class CacheRegistrationExtensions
{
    /// <summary>
    /// Register a concrete <see cref="ICacheStore"/> implementation. Equivalent to:
    /// <code>
    /// services.TryAddSingleton&lt;TStore&gt;();
    /// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ICacheStore, TStore&gt;(
    ///     sp =&gt; sp.GetRequiredService&lt;TStore&gt;()));
    /// </code>
    /// </summary>
    public static IServiceCollection AddCacheStore<TStore>(this IServiceCollection services)
        where TStore : class, ICacheStore
    {
        services.TryAddSingleton<TStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheStore, TStore>(sp => sp.GetRequiredService<TStore>()));
        return services;
    }

    /// <summary>
    /// Register a concrete <see cref="ICacheCoherenceChannel"/> implementation. Mirrors
    /// <see cref="AddCacheStore{TStore}"/> for the coherence channel collection.
    /// </summary>
    public static IServiceCollection AddCoherenceChannel<TChannel>(this IServiceCollection services)
        where TChannel : class, ICacheCoherenceChannel
    {
        services.TryAddSingleton<TChannel>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCoherenceChannel, TChannel>(sp => sp.GetRequiredService<TChannel>()));
        return services;
    }
}
