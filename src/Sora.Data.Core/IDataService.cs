using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Core;

/// <summary>
/// Provides access to aggregate repositories resolved from configured adapters.
/// Acts as a thin service-layer entry point used by high-level extensions.
/// </summary>
public interface IDataService
{
    /// <summary>
    /// Get a repository for the specified aggregate and key type.
    /// Implementations may cache resolved repositories for performance.
    /// </summary>
    IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Escape-hatch entry for direct commands against a named source or adapter.
    /// Returns a session for running ad-hoc queries/commands with optional connection override.
    /// </summary>
    Direct.IDirectSession Direct(string sourceOrAdapter);

    // Vector repository accessor (optional adapter). Returns null if no vector adapter is configured for the entity.
    IVectorSearchRepository<TEntity, TKey>? TryGetVectorRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}