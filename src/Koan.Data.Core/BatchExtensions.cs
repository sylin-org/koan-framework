using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public static class BatchExtensions
{
    public static Task<BatchResult> Save<TEntity, TKey>(
        this IBatchSet<TEntity, TKey> batch,
        BatchOptions? options = null,
        CancellationToken ct = default)
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => batch.SaveAsync(options, ct);
}
