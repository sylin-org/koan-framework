namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Storage naming for an adapter. An adapter <em>announces</em> its naming constraints via
/// <see cref="GetNamingCapability"/> (style, separators, casing, partition rules, identifier limit); the
/// framework (<see cref="StorageNameGenerator"/>) owns the actual name generation and caching. This keeps
/// naming a single, declarative source of truth — no per-adapter ResolveStorage logic to drift.
/// </summary>
public interface INamingProvider
{
    /// <summary>Provider key (e.g., "sqlite", "mongo", "weaviate").</summary>
    string Provider { get; }

    /// <summary>
    /// Announce this adapter's naming constraints. Called by the framework on cache miss; may read the
    /// adapter's options from <paramref name="services"/>.
    /// </summary>
    StorageNamingCapability GetNamingCapability(System.IServiceProvider services);

    /// <summary>
    /// Resolve the final storage identifier for an entity, optionally scoped by partition. Default
    /// implementation delegates to the shared <see cref="StorageNameGenerator"/> using the announced
    /// capability — adapters should not override this.
    /// </summary>
    string ResolveStorage(System.Type entityType, string? partition, System.IServiceProvider services)
        => StorageNameGenerator.Resolve(Provider, entityType, partition, () => GetNamingCapability(services));
}
