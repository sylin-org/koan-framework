namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Storage naming for an adapter. Each adapter owns its naming end-to-end —
/// attribute precedence, convention-based fallback, partition suffix or native container,
/// sanitization, identifier limits — and caches results in whatever scope makes sense
/// (per-factory-instance preferred, since adapter factories are DI singletons).
/// </summary>
public interface INamingProvider
{
    /// <summary>Provider key (e.g., "sqlite", "mongo", "weaviate").</summary>
    string Provider { get; }

    /// <summary>
    /// Resolve final storage identifier for an entity, optionally suffixed by partition.
    /// Adapter is responsible for caching (per-factory is the canonical choice).
    /// </summary>
    string ResolveStorage(System.Type entityType, string? partition, System.IServiceProvider services);
}
