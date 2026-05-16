namespace Koan.Data.Abstractions;

/// <summary>
/// A fluent accumulator for batched entity operations. Queue multiple adds, updates, and deletes,
/// then commit them in a single <see cref="Save"/> call.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this batch set.</typeparam>
/// <typeparam name="TKey">The entity identifier type.</typeparam>
/// <remarks>
/// Atomicity guarantees depend on the underlying provider. Relational providers (Postgres, SQL Server)
/// wrap the batch in a transaction. Document providers (MongoDB) execute operations sequentially
/// without a distributed transaction unless the provider explicitly supports multi-document sessions.
/// Batch size is not limited by the interface, but very large batches (&gt;1 000 items) should be
/// split to stay within provider limits and avoid memory pressure.
/// </remarks>
public interface IBatchSet<TEntity, TKey>
{
    /// <summary>Queues an insert for <paramref name="entity"/>.</summary>
    IBatchSet<TEntity, TKey> Add(TEntity entity);

    /// <summary>Queues an upsert for <paramref name="entity"/> (insert or replace by Id).</summary>
    IBatchSet<TEntity, TKey> Update(TEntity entity);

    /// <summary>
    /// Queues an update by loading the entity with <paramref name="id"/> and applying
    /// <paramref name="mutate"/> before saving. The load is deferred until <see cref="Save"/>.
    /// </summary>
    IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate);

    /// <summary>Queues a delete for the entity with <paramref name="id"/>.</summary>
    IBatchSet<TEntity, TKey> Delete(TKey id);

    /// <summary>Removes all queued operations without committing.</summary>
    IBatchSet<TEntity, TKey> Clear();

    /// <summary>
    /// Commits all queued operations to the store.
    /// </summary>
    /// <param name="options">Optional batch execution options (timeout, conflict strategy, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="BatchResult"/> summarising the operations performed.</returns>
    Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default);
}