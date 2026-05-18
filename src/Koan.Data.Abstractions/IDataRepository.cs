using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    /// <summary>
    /// Idempotent. Ensures the backing store is provisioned and reachable. Called by the
    /// repository facade before any data operation; adapters cache their own readiness state
    /// (per-instance, naturally bounded to the current ServiceProvider's lifetime).
    /// </summary>
    /// <remarks>
    /// Default implementation is a no-op. Adapters that need provisioning (relational schema,
    /// Couchbase scope/collection, etc.) override and run their setup with whatever caching /
    /// retry policy makes sense for them.
    /// </remarks>
    Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;

    Task<TEntity?> Get(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    // Query is handled via the typed interfaces ILinqQueryRepositoryWithOptions and
    // IStringQueryRepositoryWithOptions. The base interface used to expose an untyped
    // Query(object?, ct) slot — six adapters silently returned the full set when they didn't
    // dispatch on Expression<> in a runtime switch. Removed in DATA-0095 Phase 1b.
    Task<CountResult> Count(CountRequest<TEntity> request, CancellationToken ct = default);
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
