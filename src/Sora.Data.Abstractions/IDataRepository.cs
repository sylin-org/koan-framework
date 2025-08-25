namespace Sora.Data.Abstractions;

public interface IDataRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default);
    Task<int> CountAsync(object? query, CancellationToken ct = default);
    Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);

    Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default);
    // Deletes all entities in the current set; should return the number of deleted records
    Task<int> DeleteAllAsync(CancellationToken ct = default);

    IBatchSet<TEntity, TKey> CreateBatch();
}