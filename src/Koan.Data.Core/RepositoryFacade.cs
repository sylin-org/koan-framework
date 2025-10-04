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

    private Task EnsureSchemaAsync(CancellationToken ct) => _schemaGuard.EnsureHealthyAsync(ct);

    private async Task GuardAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureSchemaAsync(ct).ConfigureAwait(false);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.GetAsync(id, ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.QueryAsync(query, ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IDataRepositoryWithOptions<TEntity, TKey> with)
            return await with.QueryAsync(query, options, ct).ConfigureAwait(false);
        // Fallback: ignore options and use base method; adapters will apply guardrails
        return await _inner.QueryAsync(query, ct).ConfigureAwait(false);
    }
    public async Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.CountAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is ILinqQueryRepository<TEntity, TKey> linq)
            return await linq.QueryAsync(predicate, ct).ConfigureAwait(false);
        throw new NotSupportedException("LINQ queries are not supported by this repository.");
    }
    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is ILinqQueryRepositoryWithOptions<TEntity, TKey> linq)
            return await linq.QueryAsync(predicate, options, ct).ConfigureAwait(false);
        if (_inner is ILinqQueryRepository<TEntity, TKey> linqb)
            return await linqb.QueryAsync(predicate, ct).ConfigureAwait(false);
        throw new NotSupportedException("LINQ queries are not supported by this repository.");
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IStringQueryRepository<TEntity, TKey> raw)
            return await raw.QueryAsync(query, ct).ConfigureAwait(false);
        throw new NotSupportedException("String queries are not supported by this repository.");
    }
    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, DataQueryOptions? options, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> raw)
            return await raw.QueryAsync(query, options, ct).ConfigureAwait(false);
        if (_inner is IStringQueryRepository<TEntity, TKey> rawb)
            return await rawb.QueryAsync(query, ct).ConfigureAwait(false);
        throw new NotSupportedException("String queries are not supported by this repository.");
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> rawp)
            return await rawp.QueryAsync(query, parameters, null, ct).ConfigureAwait(false);
        throw new NotSupportedException("Parameterized string queries are not supported by this repository.");
    }
    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> rawp)
            return await rawp.QueryAsync(query, parameters, options, ct).ConfigureAwait(false);
        throw new NotSupportedException("Parameterized string queries with options are not supported by this repository.");
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        await _manager.EnsureIdAsync<TEntity, TKey>(model, ct).ConfigureAwait(false);

        // Auto-update [Timestamp] field if present
        if (_timestampBag.HasTimestamp)
            _timestampBag.UpdateTimestamp(model);

        return await _inner.UpsertAsync(model, ct).ConfigureAwait(false);
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        // Materialize to avoid double enumeration creating fresh instances (e.g., LINQ Select)
        var list = models as IList<TEntity> ?? models.ToList();
        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            await _manager.EnsureIdAsync<TEntity, TKey>(m, ct).ConfigureAwait(false);

            // Auto-update [Timestamp] field if present
            if (_timestampBag.HasTimestamp)
                _timestampBag.UpdateTimestamp(m);
        }
        return await _inner.UpsertManyAsync(list, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.DeleteAsync(id, ct).ConfigureAwait(false);
    }
    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.DeleteManyAsync(ids, ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        // Prefer adapter fast-path when available
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            try { return await exec.ExecuteAsync<int>(new Instruction(DataInstructions.Clear), ct).ConfigureAwait(false); }
            catch (NotSupportedException) { /* fall back */ }
        }
        // Fallback: enumerate ids then delete
        var all = await _inner.QueryAsync(null, ct).ConfigureAwait(false);
        var ids = all.Select(e => e.Id);
        return await _inner.DeleteManyAsync(ids, ct).ConfigureAwait(false);
    }

    public async Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        return await _inner.RemoveAllAsync(strategy, ct).ConfigureAwait(false);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new BatchFacade(this);

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            return await exec.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }

    public Task EnsureHealthyAsync(CancellationToken ct)
    {
        if (_inner is ISchemaHealthContributor<TEntity, TKey> contributor)
        {
            return contributor.EnsureHealthyAsync(ct);
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
        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            await _outer.GuardAsync(ct).ConfigureAwait(false);
            foreach (var e in _adds)
            {
                ct.ThrowIfCancellationRequested();
                await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct).ConfigureAwait(false);
            }
            foreach (var e in _updates)
            {
                ct.ThrowIfCancellationRequested();
                await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct).ConfigureAwait(false);
            }
            // apply queued mutations by loading current entity, mutating, and queuing as update
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    ct.ThrowIfCancellationRequested();
                    var current = await _outer._inner.GetAsync(id, ct).ConfigureAwait(false);
                    if (current is not null)
                    {
                        mutate(current);
                        await _outer._manager.EnsureIdAsync<TEntity, TKey>(current, ct).ConfigureAwait(false);
                        _updates.Add(current);
                    }
                }
            }

            // Delegate to adapter-native batch to enable provider semantics (e.g., transactions, accurate counts)
            var native = _outer._inner.CreateBatch();
            foreach (var e in _adds) native.Add(e);
            foreach (var e in _updates) native.Update(e);
            foreach (var id in _deletes) native.Delete(id);
            return await native.SaveAsync(options, ct).ConfigureAwait(false);
        }
    }
}
