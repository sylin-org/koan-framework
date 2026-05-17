using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Metadata;
using Koan.Data.Core.Schema;
using System.Linq.Expressions;

namespace Koan.Data.Core;

/// <summary>
/// Adds cross-cutting behaviors on top of an underlying repository:
/// - Ensures identifiers for all upserts (single, many, batch)
/// - Auto-updates [Timestamp] fields on save operations
/// - Advertises query/write capabilities
/// - Bridges optional LINQ and raw-string querying
/// - Forwards instruction execution when supported by the adapter
///</summary>
internal sealed class RepositoryFacade<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IInstructionExecutor<TEntity>,
    ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IAggregateIdentityManager _manager;
    private readonly EntitySchemaGuard<TEntity, TKey> _schemaGuard;
    private readonly TimestampPropertyBag _timestampBag;
    private readonly QueryCapabilities _caps;
    private readonly WriteCapabilities _writeCaps;
    /// <summary>
    /// Create a facade over a repository with identity management and timestamp auto-update.
    /// </summary>
    public RepositoryFacade(IDataRepository<TEntity, TKey> inner, IAggregateIdentityManager manager, EntitySchemaGuard<TEntity, TKey> schemaGuard)
    {
        _inner = inner; _manager = manager; _schemaGuard = schemaGuard;
        _timestampBag = new TimestampPropertyBag(typeof(TEntity));
        _caps = inner is IQueryCapabilities qc ? qc.Capabilities : QueryCapabilities.None;
        _writeCaps = inner is IWriteCapabilities wc ? wc.Writes : WriteCapabilities.None;
    }

    public QueryCapabilities Capabilities => _caps;
    public WriteCapabilities Writes => _writeCaps;

    private Task EnsureSchema(CancellationToken ct) => _schemaGuard.EnsureHealthy(ct);

    private async Task Guard(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureSchema(ct);
    }

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.Get(id, ct);
    }
    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.GetMany(ids, ct);
    }
    public async Task<IReadOnlyList<TEntity>> Query(object? query, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.Query(query, ct);
    }
    public async Task<RepositoryQueryResult<TEntity>> Query(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IDataRepositoryWithOptions<TEntity, TKey> with)
            return await with.Query(query, options, ct);
        // Fallback: ignore options and use base method; orchestrator handles sort/page in memory
        var items = await _inner.Query(query, ct);
        return RepositoryQueryResult<TEntity>.Unhandled(items);
    }
    public async Task<CountResult> Count(CountRequest<TEntity> request, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.Count(request, ct);
    }

    public async Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is ILinqQueryRepository<TEntity, TKey> linq)
            return await linq.Query(predicate, ct);
        throw new NotSupportedException("LINQ queries are not supported by this repository.");
    }
    public async Task<RepositoryQueryResult<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is ILinqQueryRepositoryWithOptions<TEntity, TKey> linq)
            return await linq.Query(predicate, options, ct);
        if (_inner is ILinqQueryRepository<TEntity, TKey> linqb)
        {
            var items = await linqb.Query(predicate, ct);
            return RepositoryQueryResult<TEntity>.Unhandled(items);
        }
        throw new NotSupportedException("LINQ queries are not supported by this repository.");
    }

    public async Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IStringQueryRepository<TEntity, TKey> raw)
            return await raw.Query(query, ct);
        throw new NotSupportedException("String queries are not supported by this repository.");
    }
    public async Task<RepositoryQueryResult<TEntity>> Query(string query, DataQueryOptions? options, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> raw)
            return await raw.Query(query, options, ct);
        if (_inner is IStringQueryRepository<TEntity, TKey> rawb)
        {
            var items = await rawb.Query(query, ct);
            return RepositoryQueryResult<TEntity>.Unhandled(items);
        }
        throw new NotSupportedException("String queries are not supported by this repository.");
    }

    public async Task<IReadOnlyList<TEntity>> Query(string query, object? parameters, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> rawp)
        {
            var result = await rawp.Query(query, parameters, null, ct);
            return result.Items;
        }
        throw new NotSupportedException("Parameterized string queries are not supported by this repository.");
    }
    public async Task<RepositoryQueryResult<TEntity>> Query(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> rawp)
            return await rawp.Query(query, parameters, options, ct);
        throw new NotSupportedException("Parameterized string queries with options are not supported by this repository.");
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        await Guard(ct);
        await _manager.EnsureIdAsync<TEntity, TKey>(model, ct);

        // Auto-update [Timestamp] field if present
        if (_timestampBag.HasTimestamp)
            _timestampBag.UpdateTimestamp(model);

        return await _inner.Upsert(model, ct);
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await Guard(ct);
        // Materialize to avoid double enumeration creating fresh instances (e.g., LINQ Select)
        var list = models as IList<TEntity> ?? models.ToList();
        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            await _manager.EnsureIdAsync<TEntity, TKey>(m, ct);

            // Auto-update [Timestamp] field if present
            if (_timestampBag.HasTimestamp)
                _timestampBag.UpdateTimestamp(m);
        }
        return await _inner.UpsertMany(list, ct);
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.Delete(id, ct);
    }
    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.DeleteMany(ids, ct);
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await Guard(ct);
        // Prefer adapter fast-path when available
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            try { return await exec.ExecuteAsync<int>(new Instruction(DataInstructions.Clear), ct); }
            catch (NotSupportedException) { /* fall back */ }
        }
        // Fallback: enumerate ids then delete
        var all = await _inner.Query(null, ct);
        var ids = all.Select(e => e.Id);
        return await _inner.DeleteMany(ids, ct);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.RemoveAll(strategy, ct);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new BatchFacade(this);

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            return await exec.ExecuteAsync<TResult>(instruction, ct);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }

    public Task EnsureHealthy(CancellationToken ct)
    {
        if (_inner is ISchemaHealthContributor<TEntity, TKey> contributor)
        {
            return contributor.EnsureHealthy(ct);
        }
        return Task.CompletedTask;
    }

    public void InvalidateHealth()
    {
        if (_inner is ISchemaHealthContributor<TEntity, TKey> contributor)
        {
            contributor.InvalidateHealth();
        }
    }

    /// <summary>
    /// Batching facade that queues adds/updates/deletes and persists via the inner repository.
    /// Ensures ids on upserts and supports "update by mutation" for convenience.
    /// </summary>
    private sealed class BatchFacade : IBatchSet<TEntity, TKey>
    {
        private readonly RepositoryFacade<TEntity, TKey> _outer;
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public BatchFacade(RepositoryFacade<TEntity, TKey> outer) => _outer = outer;

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        /// <inheritdoc />
        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            await _outer.Guard(ct);
            foreach (var e in _adds)
            {
                ct.ThrowIfCancellationRequested();
                await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct);
            }
            foreach (var e in _updates)
            {
                ct.ThrowIfCancellationRequested();
                await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct);
            }
            // apply queued mutations by loading current entity, mutating, and queuing as update
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    ct.ThrowIfCancellationRequested();
                    var current = await _outer._inner.Get(id, ct);
                    if (current is not null)
                    {
                        mutate(current);
                        await _outer._manager.EnsureIdAsync<TEntity, TKey>(current, ct);
                        _updates.Add(current);
                    }
                }
            }

            // Delegate to adapter-native batch to enable provider semantics (e.g., transactions, accurate counts)
            var native = _outer._inner.CreateBatch();
            foreach (var e in _adds) native.Add(e);
            foreach (var e in _updates) native.Update(e);
            foreach (var id in _deletes) native.Delete(id);
            return await native.Save(options, ct);
        }
    }
}
