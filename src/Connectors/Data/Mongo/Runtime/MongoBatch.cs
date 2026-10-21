using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoBatch<TEntity, TKey>(
    Func<
        IReadOnlyList<TEntity>,
        IReadOnlyList<TEntity>,
        IReadOnlyList<(TKey Id, Action<TEntity> Mutate)>,
        IReadOnlyList<(TKey Id, Filter? Guard)>,
        BatchOptions?,
        CancellationToken,
        Task<BatchResult>> commit) : IBatchSet<TEntity, TKey>, IConditionalBatchDelete<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly List<TEntity> _adds = [];
    private readonly List<TEntity> _updates = [];
    private readonly List<(TKey Id, Action<TEntity> Mutate)> _mutations = [];
    private readonly List<(TKey Id, Filter? Guard)> _deletes = [];

    public BatchExecutionCapabilities ExecutionCapabilities => BatchExecutionCapabilities.None;

    public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
    public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
    public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
    public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add((id, null)); return this; }

    public IBatchSet<TEntity, TKey> DeleteIf(TKey id, Filter guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _deletes.Add((id, guard));
        return this;
    }

    public IBatchSet<TEntity, TKey> Clear()
    {
        _adds.Clear();
        _updates.Clear();
        _mutations.Clear();
        _deletes.Clear();
        return this;
    }

    public Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default) =>
        commit(_adds, _updates, _mutations, _deletes, options, ct);
}
