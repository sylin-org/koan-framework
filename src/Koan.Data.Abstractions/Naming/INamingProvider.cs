namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Provider-specific naming strategy for storage and partition identifiers.
/// Implemented by IDataAdapterFactory - all adapters MUST provide naming logic.
/// </summary>
public interface INamingProvider
{
    /// <summary>Provider key (e.g., "sqlite", "mongo", "weaviate").</summary>
    string Provider { get; }

    /// <summary>
    /// Get base storage name for entity type.
    /// Framework caches result per (entity, provider).
    ///
    /// Adapter can implement any logic:
    /// - Respect [Storage(Name)] / [StorageName] attributes
    /// - Apply adapter-specific conventions (e.g., MongoOptions.CollectionName)
    /// - Use StorageNameResolver.Resolve() for convention-based naming
    /// - Custom logic (legacy prefixes, multi-table mapping, etc.)
    ///
    /// Framework trims output before final composition.
    /// </summary>
    string GetStorageName(Type entityType, IServiceProvider services);

    /// <summary>
    /// Format abstract partition name into concrete provider-specific identifier.
    /// Pure function - no caching, no side effects.
    ///
    /// Framework trims output before final composition.
    ///
    /// Examples for partition "6caab928-3952-48a1-ac60-b1d2a1245c9e":
    /// - SQLite:   "6caab928395248a1ac60b1d2a1245c9e" (remove hyphens)
    /// - Weaviate: "6caab928_3952_48a1_ac60_b1d2a1245c9e" (hyphens to underscores)
    /// - MongoDB:  "6caab928-3952-48a1-ac60-b1d2a1245c9e" (pass-through)
    /// </summary>
    string GetConcretePartition(string partition);

    /// <summary>
    /// Separator between storage name and partition identifier.
    ///
    /// Examples:
    /// - SQLite:   "#" → "MyApp.Todo#6caab928395248a1ac60b1d2a1245c9e"
    /// - Weaviate: "_" → "MyApp_Todo_6caab928_3952_48a1_ac60_b1d2a1245c9e"
    /// - MongoDB:  "#" → "MyApp.Todo#6caab928-3952-48a1-ac60-b1d2a1245c9e"
    /// </summary>
    string RepositorySeparator { get; }
}
