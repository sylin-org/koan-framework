using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public static class EntityPartitionMoveExtensions
{
    public static async Task MoveToPartition<TEntity, TKey>(this TEntity model, string toPartition, string? fromPartition = null, bool copy = false, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Upsert into target
        await Data<TEntity, TKey>.UpsertAsync(model, toPartition, ct).ConfigureAwait(false);
        if (!copy)
        {
            var from = fromPartition ?? EntityContext.Current?.Partition;
            await Data<TEntity, TKey>.DeleteAsync(model.Id, from ?? "root", ct).ConfigureAwait(false);
        }
    }
}