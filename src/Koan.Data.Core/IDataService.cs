using Koan.Data.Abstractions;
using Koan.Data.Core.Axes;
namespace Koan.Data.Core;

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
    /// Resolve the <b>undecorated</b> read-scope diagnostic for an aggregate (ARCH-0101 §8/§9) — the facade itself,
    /// the authority that holds the raw adapter for the capability / <c>IQueryRepository</c> inspection.
    /// <see cref="DataAxis.Explain"/> uses it (and so does the §8 boot-refuses-leaky-axis pre-flight); not a hot path.
    /// </summary>
    IAxisScopeDiagnostics GetScopeDiagnostics<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Escape-hatch entry for direct commands against a named source or adapter.
    /// Returns a session for running ad-hoc queries/commands with optional connection override.
    /// Specify either source OR adapter, not both (source XOR adapter constraint).
    /// </summary>
    Direct.IDirectSession Direct(string? source = null, string? adapter = null);

}