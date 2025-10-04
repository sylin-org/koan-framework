using System.Collections.Concurrent;
using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// In-memory repository with full LINQ support and thread-safe operations.
/// Supports partition isolation for multi-tenant scenarios.
/// </summary>
internal sealed class InMemoryRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
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

    /// <summary>
    /// InMemory adapter supports full LINQ-to-Objects capabilities.
    /// </summary>
    public QueryCapabilities Capabilities => QueryCapabilities.Linq;

    /// <summary>
    /// InMemory adapter supports all write operations with atomic batches.
    /// </summary>
    public WriteCapabilities Writes =>
        WriteCapabilities.BulkUpsert |
        WriteCapabilities.BulkDelete |
        WriteCapabilities.AtomicBatch;

    /// <summary>
    /// Resolves the current partition from EntityContext, always returning a valid partition name.
    /// This ensures partition isolation is respected even when repositories are cached.
    /// </summary>
    private string CurrentPartition =>
        Koan.Data.Core.EntityContext.Current?.Partition ?? "default";

    private ConcurrentDictionary<TKey, TEntity> Store =>
        _dataStore.GetOrCreateStore<TEntity, TKey>(CurrentPartition);

    // ==================== Read Operations ====================

    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store.TryGetValue(id, out var value) ? value : null);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = Store.Values.ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)result);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var items = Store.Values.AsQueryable();

        // Apply LINQ predicate if provided
        if (query is Expression<Func<TEntity, bool>> predicate)
        {
            items = items.Where(predicate);
        }

        items = ApplyOptions(items, options);
        return Task.FromResult((IReadOnlyList<TEntity>)items.ToList());
    }

    // ==================== LINQ Query Operations ====================

    public Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IQueryable<TEntity> items = Store.Values.AsQueryable();

        if (request.Predicate is not null)
        {
            items = items.Where(request.Predicate);
        }
        else if (request.RawQuery is not null || request.ProviderQuery is not null)
        {
            throw new NotSupportedException("String or provider-specific count queries are not supported by the in-memory adapter.");
        }

        var total = items.Count();
        return Task.FromResult(new CountResult(total, false));
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = Store.Values.AsQueryable().Where(predicate).ToList();
        return Task.FromResult((IReadOnlyList<TEntity>)result);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var items = Store.Values.AsQueryable().Where(predicate);
        items = ApplyOptions(items, options);
        return Task.FromResult((IReadOnlyList<TEntity>)items.ToList());
    }

    // ==================== Write Operations ====================

    public Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Store[model.Id] = model;
        return Task.FromResult(model);
    }

    public Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
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

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store.TryRemove(id, out _));
    }

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = 0;
        foreach (var id in ids)
        {
            if (Store.TryRemove(id, out _))
                count++;
            ct.ThrowIfCancellationRequested();
        }
        return Task.FromResult(count);
    }

    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var count = Store.Count;
        Store.Clear();
        return Task.FromResult(count);
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

        public Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Apply mutations first
            foreach (var (id, mutate) in _mutations)
            {
                ct.ThrowIfCancellationRequested();
                if (_repo.Store.TryGetValue(id, out var current))
                {
                    mutate(current);
                    _updates.Add(current);
                }
            }

            // Execute batch operations
            var store = _repo.Store;
            foreach (var entity in _adds)
            {
                store[entity.Id] = entity;
                ct.ThrowIfCancellationRequested();
            }

            foreach (var entity in _updates)
            {
                store[entity.Id] = entity;
                ct.ThrowIfCancellationRequested();
            }

            var deletedCount = 0;
            foreach (var id in _deletes)
            {
                if (store.TryRemove(id, out _))
                    deletedCount++;
                ct.ThrowIfCancellationRequested();
            }

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
                    // InMemory storage is always "created" - no initialization needed
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

    // ==================== Helper Methods ====================

    private static IQueryable<TEntity> ApplyOptions(IQueryable<TEntity> items, DataQueryOptions? options)
    {
        if (options == null)
            return items;

        // Apply pagination
        if (options.Page.HasValue && options.PageSize.HasValue)
        {
            var skip = (options.Page.Value - 1) * options.PageSize.Value;
            items = items.Skip(skip).Take(options.PageSize.Value);
        }
        else if (options.PageSize.HasValue)
        {
            items = items.Take(options.PageSize.Value);
        }

        return items;
    }
}
