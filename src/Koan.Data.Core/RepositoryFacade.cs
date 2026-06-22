using System.Linq.Expressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Metadata;
using Koan.Data.Core.Tenancy;

namespace Koan.Data.Core;

/// <summary>
/// Adds cross-cutting behaviors on top of an underlying repository:
/// - Ensures identifiers for all upserts (single, many, batch)
/// - Auto-updates [Timestamp] fields on save operations
/// - Advertises query/write capabilities
/// - Bridges the structured query (<see cref="IQueryRepository{TEntity,TKey}"/>) and raw query
///   (<see cref="IRawQueryRepository{TEntity,TKey}"/>) surfaces of the inner adapter
/// - Forwards instruction execution when supported by the adapter
/// </summary>
internal sealed class RepositoryFacade<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IAggregateIdentityManager _manager;
    private readonly TimestampPropertyBag _timestampBag;
    private readonly TenantScopeMetadata _tenantScope;
    private readonly ITenantEnforcer? _tenantEnforcer;

    public RepositoryFacade(IDataRepository<TEntity, TKey> inner, IAggregateIdentityManager manager, ITenantEnforcer? tenantEnforcer = null)
    {
        _inner = inner; _manager = manager; _tenantEnforcer = tenantEnforcer;
        _timestampBag = new TimestampPropertyBag(typeof(TEntity));
        _tenantScope = new TenantScopeMetadata(typeof(TEntity));
    }

    // ARCH-0084: forward the inner provider's unified capabilities (native IDescribesCapabilities,
    // else the legacy-marker bridge) — so the facade is correct regardless of how inner declares.
    public void Describe(ICapabilities caps)
        => DataCaps.Describe(_inner, _inner.GetType().Name).CopyInto(caps);

    public Task EnsureReady(CancellationToken ct = default) => _inner.EnsureReady(ct);

    private async Task Guard(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // ARCH-0095 P1: fail-closed tenant gate at the chokepoint, BEFORE touching the store. No-op when
        // tenancy is off (default) or the entity is [HostScoped]. Read-filter/write-stamp land in the next slice.
        _tenantEnforcer?.Guard(typeof(TEntity), _tenantScope.IsHostScoped);
        await _inner.EnsureReady(ct);
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

    // --- structured query ---

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        return await RequireQuery().Query(query, ct);
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        return await RequireQuery().Count(query, ct);
    }

    private IQueryRepository<TEntity, TKey> RequireQuery()
        => _inner as IQueryRepository<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not implement IQueryRepository.");

    // --- raw query escape hatch ---

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default)
    {
        await Guard(ct);
        return _inner is IRawQueryRepository<TEntity, TKey> raw
            ? await raw.QueryRaw(query, parameters, shaping, ct)
            : throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        await Guard(ct);
        return _inner is IRawQueryRepository<TEntity, TKey> raw
            ? await raw.CountRaw(query, parameters, ct)
            : throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
    }

    // --- writes ---

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        await Guard(ct);
        await _manager.EnsureIdAsync<TEntity, TKey>(model, ct);
        if (_timestampBag.HasTimestamp) _timestampBag.UpdateTimestamp(model);
        return await _inner.Upsert(model, ct);
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await Guard(ct);
        var list = models as IList<TEntity> ?? models.ToList();
        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            await _manager.EnsureIdAsync<TEntity, TKey>(m, ct);
            if (_timestampBag.HasTimestamp) _timestampBag.UpdateTimestamp(m);
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
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            try { return await exec.ExecuteAsync<int>(new Instruction(DataInstructions.Clear), ct); }
            catch (NotSupportedException) { /* fall back */ }
        }
        var all = await RequireQuery().Query(QueryDefinition.All, ct);
        var ids = all.Items.Select(e => e.Id);
        return await _inner.DeleteMany(ids, ct);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await Guard(ct);
        return await _inner.RemoveAll(strategy, ct);
    }

    // Forward the inner adapter's conditional compare-and-set (probe via DataCaps.Write.ConditionalReplace, which the
    // facade reports from the inner). Only called when the capability was declared, so the inner supports it.
    public async Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IConditionalWriteRepository<TEntity, TKey> cas)
            return await cas.ConditionalReplaceAsync(model, guard, ct);
        throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support conditional replace.");
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new BatchFacade(this);

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_inner is IInstructionExecutor<TEntity> exec)
            return await exec.ExecuteAsync<TResult>(instruction, ct);
        throw new NotSupportedException($"Repository for {typeof(TEntity).Name} does not support instruction '{instruction.Name}'.");
    }

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

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            await _outer.Guard(ct);
            foreach (var e in _adds) { ct.ThrowIfCancellationRequested(); await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct); }
            foreach (var e in _updates) { ct.ThrowIfCancellationRequested(); await _outer._manager.EnsureIdAsync<TEntity, TKey>(e, ct); }
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

            var native = _outer._inner.CreateBatch();
            foreach (var e in _adds) native.Add(e);
            foreach (var e in _updates) native.Update(e);
            foreach (var id in _deletes) native.Delete(id);
            return await native.Save(options, ct);
        }
    }
}
