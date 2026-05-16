using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<TEntity?> Get(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(object? query, CancellationToken ct = default);
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
