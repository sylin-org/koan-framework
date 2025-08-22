using System.Linq.Expressions;
using Sora.Data.Abstractions;

namespace Sora.Data.Core;

public sealed class SetMoveBuilder<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly string _from;
    private Expression<Func<TEntity, bool>>? _predicate;
    private Func<TEntity, TEntity>? _map;
    private bool _copyOnly;
    private int _batchSize = 500;

    internal SetMoveBuilder(string from) => _from = from;
    public SetMoveBuilder<TEntity, TKey> Where(Expression<Func<TEntity, bool>> predicate) { _predicate = predicate; return this; }
    public SetMoveBuilder<TEntity, TKey> Map(Func<TEntity, TEntity> transform) { _map = transform; return this; }
    public SetMoveBuilder<TEntity, TKey> Copy() { _copyOnly = true; return this; }
    public SetMoveBuilder<TEntity, TKey> BatchSize(int size) { if (size > 0) _batchSize = size; return this; }
    public Task<int> To(string toSet, CancellationToken ct = default)
        => _copyOnly
            ? Data<TEntity, TKey>.CopySet(_from, toSet, _predicate, _map, _batchSize, ct)
            : Data<TEntity, TKey>.MoveSet(_from, toSet, _predicate, _map, _batchSize, ct);
}