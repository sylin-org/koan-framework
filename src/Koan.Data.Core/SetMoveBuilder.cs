using Koan.Data.Abstractions;
using System.Linq.Expressions;

namespace Koan.Data.Core;

public sealed class PartitionMoveBuilder<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly string _from;
    private Expression<Func<TEntity, bool>>? _predicate;
    private Func<TEntity, TEntity>? _map;
    private bool _copyOnly;
    private int _batchSize = 500;

    internal PartitionMoveBuilder(string from) => _from = from;
    public PartitionMoveBuilder<TEntity, TKey> Where(Expression<Func<TEntity, bool>> predicate) { _predicate = predicate; return this; }
    public PartitionMoveBuilder<TEntity, TKey> Map(Func<TEntity, TEntity> transform) { _map = transform; return this; }
    public PartitionMoveBuilder<TEntity, TKey> Copy() { _copyOnly = true; return this; }
    public PartitionMoveBuilder<TEntity, TKey> BatchSize(int size) { if (size > 0) _batchSize = size; return this; }
    public Task<int> To(string toPartition, CancellationToken ct = default)
        => _copyOnly
            ? Data<TEntity, TKey>.CopyPartition(_from, toPartition, _predicate, _map, _batchSize, ct)
            : Data<TEntity, TKey>.MovePartition(_from, toPartition, _predicate, _map, _batchSize, ct);
}