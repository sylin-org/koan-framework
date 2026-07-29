using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteBatch<TEntity, TKey>(
    Func<
        IReadOnlyList<TEntity>,
        IReadOnlyList<TEntity>,
        IReadOnlyList<(TKey Id, Action<TEntity> Mutate)>,
        IReadOnlyList<TKey>,
        BatchOptions?,
        CancellationToken,
        Task<BatchResult>> commit) : IBatchSet<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly List<TEntity> _adds = [];
    private readonly List<TEntity> _updates = [];
    private readonly List<(TKey Id, Action<TEntity> Mutate)> _mutations = [];
    private readonly List<TKey> _deletes = [];

    public BatchExecutionCapabilities ExecutionCapabilities => BatchExecutionCapabilities.Atomic;

    public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
    public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
    public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
    public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }

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
