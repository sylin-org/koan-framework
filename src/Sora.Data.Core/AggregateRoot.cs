using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Data.Core.Model;

// CRTP base providing static conveniences on the derived type
public abstract class Data<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    [Identifier]
    public TKey Id { get; set; } = default!;

    // Static conveniences resolve via Data<TEntity, TKey>
    public static Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.GetAsync(id, ct);

    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.All(ct);
    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Sora.Data.Core.Data<TEntity, TKey>.Query(query, ct);

    public static IBatchSet<TEntity, TKey> Batch() => Sora.Data.Core.Data<TEntity, TKey>.Batch();
}
