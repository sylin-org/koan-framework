using Sora.Data.Abstractions;

namespace Sora.Data.Core;

public static class EntitySetMoveExtensions
{
    public static async Task MoveToSet<TEntity, TKey>(this TEntity model, string toSet, string? fromSet = null, bool copy = false, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // Upsert into target
        await Data<TEntity, TKey>.UpsertAsync(model, toSet, ct).ConfigureAwait(false);
        if (!copy)
        {
            var from = fromSet ?? DataSetContext.Current;
            await Data<TEntity, TKey>.DeleteAsync(model.Id, from ?? "root", ct).ConfigureAwait(false);
        }
    }
}