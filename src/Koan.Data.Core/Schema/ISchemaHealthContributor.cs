using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Schema;

/// <summary>
/// Adapter hook that exposes schema health orchestration to the entity-level guard.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TKey">Entity key type.</typeparam>
public interface ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Ensure the backing store for the current entity (and active set) is healthy.
    /// </summary>
    Task EnsureHealthyAsync(CancellationToken ct);

    /// <summary>
    /// Invalidate any adapter-local caches so the next ensure recomputes schema health.
    /// </summary>
    void InvalidateHealth();
}
