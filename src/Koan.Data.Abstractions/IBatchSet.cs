namespace Koan.Data.Abstractions;

public interface IBatchSet<TEntity, TKey>
{
    IBatchSet<TEntity, TKey> Add(TEntity entity);
    IBatchSet<TEntity, TKey> Update(TEntity entity);
    // Convenience: queue an update by id and a mutation action to apply before saving
    IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate);
    IBatchSet<TEntity, TKey> Delete(TKey id);
    IBatchSet<TEntity, TKey> Clear();
    Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default);
}