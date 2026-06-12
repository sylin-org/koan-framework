using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

/// <summary>
/// The write/CRUD surface every data adapter implements. Querying and counting live on the
/// separate <see cref="IQueryRepository{TEntity, TKey}"/> contract (one method taking a
/// <see cref="QueryDefinition"/>); raw provider queries live on the optional
/// <see cref="IRawQueryRepository{TEntity, TKey}"/> escape hatch.
/// </summary>
public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    /// <summary>
    /// Idempotent. Ensures the backing store is provisioned and reachable. Called by the
    /// repository facade before any data operation; adapters cache their own readiness state.
    /// Default implementation is a no-op.
    /// </summary>
    Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;

    Task<TEntity?> Get(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    Task<TEntity> Upsert(TEntity model, CancellationToken ct = default);
    Task<bool> Delete(TKey id, CancellationToken ct = default);

    Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default);
    Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    // Deletes all entities in the current set; should return the number of deleted records
    Task<int> DeleteAll(CancellationToken ct = default);
    // Removes all entities using the specified strategy; returns count of deleted records, or -1 if unknown
    Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default);

    IBatchSet<TEntity, TKey> CreateBatch();
}
