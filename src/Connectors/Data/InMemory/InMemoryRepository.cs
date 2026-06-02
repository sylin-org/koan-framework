using System.Collections.Concurrent;
using System.Collections.Frozen;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// In-memory repository with full LINQ-to-objects support and thread-safe operations.
/// Reference "Full floor" adapter under the unified query contract (DATA-XXXX): it declares
/// <see cref="FilterCapabilities.Full"/> and evaluates the entire <see cref="Filter"/> via
/// <see cref="InMemoryFilterEvaluator"/> — so the coordinator never produces a residual for it.
/// Sort and pagination are handled natively (trivially, in memory). Supports partition isolation.
/// </summary>
internal sealed class InMemoryRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryRepository(InMemoryDataStore dataStore, string _)
    {
        // Note: Partition parameter ignored - always resolved dynamically from EntityContext
        _dataStore = dataStore;
    }

    /// <summary>InMemory supports full LINQ-to-objects capabilities.</summary>
    public QueryCapabilities Capabilities => QueryCapabilities.Linq;

    /// <summary>InMemory pushes every filter operator (it runs the AST directly in memory).</summary>
    public FilterCapabilities FilterCapabilities => FilterCapabilities.Full;

    public WriteCapabilities Writes =>
        WriteCapabilities.BulkUpsert |
        WriteCapabilities.BulkDelete |
        WriteCapabilities.AtomicBatch;

    private string CurrentPartition =>
        Koan.Data.Core.EntityContext.Current?.Partition ?? "default";

    private ConcurrentDictionary<TKey, TEntity> Store =>
        _dataStore.GetOrCreateStore<TEntity, TKey>(CurrentPartition);

    // ==================== Read Operations ====================

    public Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store.TryGetValue(id, out var value) ? value : null);
    }

    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        var results = new TEntity?[idList.Count];
        for (var i = 0; i < idList.Count; i++)
            results[i] = Store.TryGetValue(idList[i], out var entity) ? entity : null;
        return Task.FromResult((IReadOnlyList<TEntity?>)results);
    }

    // ==================== Unified Query ====================

    public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<TEntity> items = Store.Values;
        if (query.Filter is not null)
            items = items.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));

        // Materialize once for a stable count + ordering.
        var filtered = items as IReadOnlyList<TEntity> ?? items.ToList();
        var totalCount = (long)filtered.Count;

        var sortHandled = RepositoryQueryResult<TEntity>.NoSortHandled;
        IEnumerable<TEntity> ordered;
        if (query.HasSort)
        {
            ordered = InMemorySorter.Apply(filtered, query.Sort);
            sortHandled = query.Sort.ToFrozenSet();
        }
        else
        {
            // Match the relational adapters (SqlServer/SQLite ORDER BY Id, Postgres ORDER BY ctid): an
            // unsorted query falls back to a stable Id order so results — and any pagination over them —
            // are deterministic instead of ConcurrentDictionary enumeration order. With GUID v7 ids this
            // is also insertion order.
            ordered = filtered.OrderBy(static e => e.Id, Comparer<TKey>.Default);
        }

        var paginationHandled = false;
        if (query.HasPagination)
        {
            var skip = (query.EffectivePage() - 1) * query.EffectivePageSize();
            ordered = ordered.Skip(skip).Take(query.EffectivePageSize());
            paginationHandled = true;
        }

        var list = ordered as IReadOnlyList<TEntity> ?? ordered.ToList();
        return Task.FromResult(new RepositoryQueryResult<TEntity>
        {
            Items = list,
            TotalCount = totalCount,
            IsEstimate = false,
            SortHandled = sortHandled,
            PaginationHandled = paginationHandled,
        });
    }

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEnumerable<TEntity> items = Store.Values;
        if (query.Filter is not null)
            items = items.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
        return Task.FromResult(new CountResult(items.LongCount(), false));
    }

    // ==================== Write Operations ====================

    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Store[model.Id] = model;
        return Task.FromResult(model);
    }

    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = 0;
        foreach (var model in models)
        {
            Store[model.Id] = model;
            count++;
            ct.ThrowIfCancellationRequested();
        }
        return Task.FromResult(count);
    }

    public Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store.TryRemove(id, out _));
    }

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = 0;
        foreach (var id in ids)
        {
            if (Store.TryRemove(id, out _)) count++;
            ct.ThrowIfCancellationRequested();
        }
        return Task.FromResult(count);
    }

    public Task<int> DeleteAll(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = Store.Count;
        Store.Clear();
        return Task.FromResult(count);
    }

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = Store.Count;
        Store.Clear();
        return Task.FromResult((long)count);
    }

    // ==================== Batch Operations ====================

    public IBatchSet<TEntity, TKey> CreateBatch() => new InMemoryBatchSet(this);

    private sealed class InMemoryBatchSet : IBatchSet<TEntity, TKey>
    {
        private readonly InMemoryRepository<TEntity, TKey> _repo;
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public InMemoryBatchSet(InMemoryRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (id, mutate) in _mutations)
            {
                ct.ThrowIfCancellationRequested();
                if (_repo.Store.TryGetValue(id, out var current)) { mutate(current); _updates.Add(current); }
            }

            var store = _repo.Store;
            foreach (var entity in _adds) { store[entity.Id] = entity; ct.ThrowIfCancellationRequested(); }
            foreach (var entity in _updates) { store[entity.Id] = entity; ct.ThrowIfCancellationRequested(); }

            var deletedCount = 0;
            foreach (var id in _deletes) { if (store.TryRemove(id, out _)) deletedCount++; ct.ThrowIfCancellationRequested(); }

            return Task.FromResult(new BatchResult(_adds.Count, _updates.Count, deletedCount));
        }
    }

    // ==================== Instruction Execution ====================

    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        switch (instruction.Name)
        {
            case DataInstructions.EnsureCreated:
            {
                object result = true;
                return Task.FromResult((TResult)result);
            }
            case DataInstructions.Clear:
            {
                var count = Store.Count;
                Store.Clear();
                object result = count;
                return Task.FromResult((TResult)result);
            }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by InMemory adapter for {typeof(TEntity).Name}.");
        }
    }
}
