using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using System.Linq.Expressions;

namespace Sora.Data.Core;

/// <summary>
/// Adds cross-cutting behaviors on top of an underlying repository:
/// - Ensures identifiers for all upserts (single, many, batch)
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
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IAggregateIdentityManager _manager;
    private readonly QueryCapabilities _caps;
    private readonly WriteCapabilities _writeCaps;
    /// <summary>
    /// Create a facade over a repository with identity management.
    /// </summary>
    public RepositoryFacade(IDataRepository<TEntity, TKey> inner, IAggregateIdentityManager manager)
    {
        _inner = inner; _manager = manager;
        _caps = inner is IQueryCapabilities qc ? qc.Capabilities : QueryCapabilities.None;
        _writeCaps = inner is IWriteCapabilities wc ? wc.Writes : WriteCapabilities.None;
    }

    public QueryCapabilities Capabilities => _caps;
    public WriteCapabilities Writes => _writeCaps;

    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default) => _inner.GetAsync(id, ct);
    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default) => _inner.QueryAsync(query, ct);
    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_inner is IDataRepositoryWithOptions<TEntity, TKey> with)
            return await with.QueryAsync(query, options, ct);
        // Fallback: ignore options and use base method; adapters will apply guardrails
        return await _inner.QueryAsync(query, ct);
    }
    public Task<int> CountAsync(object? query, CancellationToken ct = default) => _inner.CountAsync(query, ct);
    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => _inner is ILinqQueryRepository<TEntity, TKey> linq
            ? linq.QueryAsync(predicate, ct)
            : throw new NotSupportedException("LINQ queries are not supported by this repository.");
    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_inner is ILinqQueryRepositoryWithOptions<TEntity, TKey> linq)
            return await linq.QueryAsync(predicate, options, ct);
        if (_inner is ILinqQueryRepository<TEntity, TKey> linqb)
            return await linqb.QueryAsync(predicate, ct);
        throw new NotSupportedException("LINQ queries are not supported by this repository.");
    }
    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => _inner is ILinqQueryRepository<TEntity, TKey> linq
            ? linq.CountAsync(predicate, ct)
            : throw new NotSupportedException("LINQ queries are not supported by this repository.");

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default)
        => _inner is IStringQueryRepository<TEntity, TKey> raw
            ? raw.QueryAsync(query, ct)
            : throw new NotSupportedException("String queries are not supported by this repository.");
    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> raw)
            return await raw.QueryAsync(query, options, ct);
        if (_inner is IStringQueryRepository<TEntity, TKey> rawb)
            return await rawb.QueryAsync(query, ct);
        throw new NotSupportedException("String queries are not supported by this repository.");
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default)
        => _inner is IStringQueryRepository<TEntity, TKey> rawp
            ? rawp.QueryAsync(query, parameters, ct)
            : throw new NotSupportedException("String queries are not supported by this repository.");
    public async Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_inner is IStringQueryRepositoryWithOptions<TEntity, TKey> rawp)
            return await rawp.QueryAsync(query, parameters, options, ct);
        if (_inner is IStringQueryRepository<TEntity, TKey> rawpb)
            return await rawpb.QueryAsync(query, parameters, ct);
        throw new NotSupportedException("String queries are not supported by this repository.");
    }
    public Task<int> CountAsync(string query, CancellationToken ct = default)
        => _inner is IStringQueryRepository<TEntity, TKey> rawc
            ? rawc.CountAsync(query, ct)
            : throw new NotSupportedException("String queries are not supported by this repository.");
    public Task<int> CountAsync(string query, object? parameters, CancellationToken ct = default)
        => _inner is IStringQueryRepository<TEntity, TKey> rawcp
            ? rawcp.CountAsync(query, parameters, ct)
            : throw new NotSupportedException("String queries are not supported by this repository.");

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _manager.EnsureIdAsync<TEntity, TKey>(model, ct);
        return await _inner.UpsertAsync(model, ct);
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        // Materialize to avoid double enumeration creating fresh instances (e.g., LINQ Select)
        var list = models as IList<TEntity> ?? models.ToList();
        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            await _manager.EnsureIdAsync<TEntity, TKey>(m, ct);
        }
        return await _inner.UpsertManyAsync(list, ct);
    }

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _inner.DeleteAsync(id, ct);
    }
    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _inner.DeleteManyAsync(ids, ct);
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Prefer adapter fast-path when available
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            try { return await exec.ExecuteAsync<int>(new Instruction(global::Sora.Data.DataInstructions.Clear), ct); }
            catch (NotSupportedException) { /* fall back */ }
        }
        // Fallback: enumerate ids then delete
        var all = await _inner.QueryAsync(null, ct);
        var ids = all.Select(e => e.Id);
        return await _inner.DeleteManyAsync(ids, ct);
    }
    public IBatchSet<TEntity, TKey> CreateBatch() => new BatchFacade(this);

    /// <inheritdoc/>
    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            return exec.ExecuteAsync<TResult>(instruction, ct);
        }
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
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
            ct.ThrowIfCancellationRequested();
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
                    var current = await _outer._inner.GetAsync(id, ct);
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
            return await native.SaveAsync(options, ct);
        }
    }
}
