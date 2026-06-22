using System.Linq.Expressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Metadata;
using Koan.Data.Core.Pipeline;

namespace Koan.Data.Core;

/// <summary>
/// Adds cross-cutting behaviors on top of an underlying repository:
/// - Ensures identifiers for all upserts (single, many, batch)
/// - Auto-updates [Timestamp] fields on save operations
/// - Advertises query/write capabilities
/// - Bridges the structured query (<see cref="IQueryRepository{TEntity,TKey}"/>) and raw query
///   (<see cref="IRawQueryRepository{TEntity,TKey}"/>) surfaces of the inner adapter
/// - Forwards instruction execution when supported by the adapter
/// - Honours <b>managed fields</b> (DATA-0105 §3b) — the invisible framework-managed isolation discriminators a
///   cross-cutting module registers (e.g. Koan.Tenancy). The facade is the gateway for the repository path: it
///   stamps the managed value on writes (the inner adapter persists + verifies it), AND-folds a managed predicate
///   into reads, lowers key-ops to managed-scoped queries (IDOR), scopes RemoveAll/DeleteAll, and fails closed on
///   the paths the managed predicate cannot cover (raw / conditional-replace / a non-isolating adapter).
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
    private readonly StorageWritePlan _writePlan;
    private readonly IStorageGuard[] _guards;
    private readonly IReadOnlyList<ManagedFieldDescriptor> _managed;
    private readonly string _idField;
    private readonly bool _managedAdapterOk;
    private readonly string? _managedAdapterError;

    public RepositoryFacade(IDataRepository<TEntity, TKey> inner, IStorageGuard[]? guards = null)
    {
        _inner = inner;
        _guards = guards ?? Array.Empty<IStorageGuard>();
        _writePlan = StorageWritePlan.For(typeof(TEntity));
        _managed = ManagedFieldRegistry.ForType(typeof(TEntity));
        _idField = AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop.Name ?? "Id";
        if (_managed.Count > 0) (_managedAdapterOk, _managedAdapterError) = InspectManagedAdapter();
        else _managedAdapterOk = true;
    }

    private bool HasManaged => _managed.Count > 0;

    // Inspect (do NOT throw at construction) whether the adapter can isolate a managed-scoped entity: it must
    // announce every required capability AND be an IQueryRepository (key-ops lower to scoped queries). We defer the
    // throw to the first operation that actually has a managed value in scope, so a non-tenant app — or a referenced-
    // but-off Koan.Tenancy on a non-isolating adapter — is a true no-op (zero regression). Fail-closed when used.
    private (bool ok, string? error) InspectManagedAdapter()
    {
        var caps = DataCaps.Describe(_inner, _inner.GetType().Name);
        foreach (var d in _managed)
        {
            if (d.RequiredCapability is { } req && !caps.Has(req))
                return (false,
                    $"Entity '{typeof(TEntity).Name}' is in an active managed scope requiring isolation capability '{req.Id}', " +
                    $"but the adapter '{_inner.GetType().Name}' does not announce it. Route it to an isolating adapter, or exempt the entity.");
        }
        if (_inner is not IQueryRepository<TEntity, TKey>)
            return (false,
                $"Entity '{typeof(TEntity).Name}' is in an active managed scope, but the adapter '{_inner.GetType().Name}' does not " +
                "implement IQueryRepository. Managed isolation lowers key operations to scoped queries, so it requires pushdown query support.");
        return (true, null);
    }

    private void RequireManagedAdapter()
    {
        if (!_managedAdapterOk) throw new InvalidOperationException(_managedAdapterError);
    }

    // --- managed-field helpers (no-op fast paths when nothing is registered / nothing is in scope) ---

    /// <summary>The managed values to stamp on the current write, or <c>null</c> when none is in scope (off / host).</summary>
    private IReadOnlyDictionary<string, object?>? CurrentManagedValues()
    {
        if (!HasManaged) return null;
        Dictionary<string, object?>? values = null;
        foreach (var d in _managed)
        {
            var v = d.ValueProvider();
            if (v is null) continue;                 // off / host scope ⇒ this field is not stamped
            (values ??= new(StringComparer.Ordinal))[d.StorageName] = v;
        }
        if (values is not null) RequireManagedAdapter();   // an active scope on a non-isolating adapter fails closed
        return values;
    }

    /// <summary>The AND-predicate isolating reads to the current managed scope, or <c>null</c> when none is in scope.</summary>
    private Filter? ManagedReadFilter()
    {
        if (!HasManaged) return null;
        List<Filter>? preds = null;
        foreach (var d in _managed)
        {
            var v = d.ValueProvider();
            if (v is null) continue;                 // off / host ⇒ unfiltered (nothing was stamped); the guard handles enforce
            (preds ??= new()).Add(Filter.Eq(d.StorageName, v));
        }
        if (preds is null) return null;
        RequireManagedAdapter();                          // an active scope on a non-isolating adapter fails closed
        return preds.Count == 1 ? preds[0] : Filter.All(preds.ToArray());
    }

    private QueryDefinition ApplyManaged(QueryDefinition query, Filter managed)
        => query.Where(query.Filter is null ? managed : Filter.All(query.Filter, managed));

    private QueryDefinition ScopedById(TKey id, Filter managed)
        => QueryDefinition.All.Where(Filter.All(Filter.Eq(_idField, id), managed));

    private QueryDefinition ScopedByIds(IReadOnlyList<TKey> ids, Filter managed)
        => QueryDefinition.All.Where(Filter.All(Filter.In(_idField, ids.Cast<object?>().ToList()), managed));

    // ARCH-0084: forward the inner provider's unified capabilities (native IDescribesCapabilities,
    // else the legacy-marker bridge) — so the facade is correct regardless of how inner declares.
    public void Describe(ICapabilities caps)
        => DataCaps.Describe(_inner, _inner.GetType().Name).CopyInto(caps);

    public Task EnsureReady(CancellationToken ct = default) => _inner.EnsureReady(ct);

    private async Task Guard(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Generic fail-closed pre-op checks at the chokepoint, BEFORE touching the store (DATA-0105 §0).
        // Cross-cutting modules register guards (Koan.Tenancy registers the tenant gate, ARCH-0095 P1); the
        // data core never names them. No registered guard ⇒ empty loop ⇒ no-op.
        for (var i = 0; i < _guards.Length; i++) _guards[i].Guard(typeof(TEntity));
        await _inner.EnsureReady(ct);
    }

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        if (managed is null) return await _inner.Get(id, ct);
        // IDOR defence: a key read is lowered to a managed-scoped query; a wrong-scope row returns null = not-found.
        var res = await RequireQuery().Query(ScopedById(id, managed), ct);
        return res.Items.Count > 0 ? res.Items[0] : null;
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        var managed = ManagedReadFilter();
        if (managed is null) return await _inner.GetMany(idList, ct);
        var res = await RequireQuery().Query(ScopedByIds(idList, managed), ct);
        var byId = new Dictionary<TKey, TEntity>();
        foreach (var e in res.Items) byId[e.Id] = e;          // owned subset
        return idList.Select(id => byId.TryGetValue(id, out var e) ? e : (TEntity?)null).ToList();
    }

    // --- structured query ---

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        return await RequireQuery().Query(managed is null ? query : ApplyManaged(query, managed), ct);
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        return await RequireQuery().Count(managed is null ? query : ApplyManaged(query, managed), ct);
    }

    private IQueryRepository<TEntity, TKey> RequireQuery()
        => _inner as IQueryRepository<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not implement IQueryRepository.");

    // --- raw query escape hatch ---
    // The managed predicate CANNOT cover an opaque raw query (DATA-0105 §3.5): RLS is the backstop. With no
    // RLS capability, a managed-scoped raw read fails closed under an active scope rather than leak cross-scope.

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default)
    {
        await Guard(ct);
        GuardRawAgainstManagedScope();
        return _inner is IRawQueryRepository<TEntity, TKey> raw
            ? await raw.QueryRaw(query, parameters, shaping, ct)
            : throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        await Guard(ct);
        GuardRawAgainstManagedScope();
        return _inner is IRawQueryRepository<TEntity, TKey> raw
            ? await raw.CountRaw(query, parameters, ct)
            : throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
    }

    private void GuardRawAgainstManagedScope()
    {
        if (HasManaged && CurrentManagedValues() is not null)
            throw new NotSupportedException(
                $"Raw queries on managed-field-scoped entity '{typeof(TEntity).Name}' are not isolated by the managed " +
                "predicate (the SQL is opaque). They are gated behind a store-level isolation backstop (e.g. RLS); with " +
                "none available the call fails closed under an active managed scope rather than read across scopes.");
    }

    // --- writes ---

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        await Guard(ct);
        _writePlan.ApplyAll(model);
        var values = CurrentManagedValues();
        if (values is null) return await _inner.Upsert(model, ct);
        // Stamp-AND-verify: the scope carries the managed values; the inner adapter injects them into the record
        // AND adds a conflict guard so an id-keyed update of a row owned by another scope is rejected (no takeover).
        using (ManagedFieldWriteScope.Enter(values))
            return await _inner.Upsert(model, ct);
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await Guard(ct);
        var list = models as IList<TEntity> ?? models.ToList();
        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            _writePlan.ApplyAll(m);
        }
        var values = CurrentManagedValues();
        if (values is null) return await _inner.UpsertMany(list, ct);
        using (ManagedFieldWriteScope.Enter(values))
            return await _inner.UpsertMany(list, ct);
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        if (managed is null) return await _inner.Delete(id, ct);
        // check-then-delete: only an owned row may be deleted by id (IDOR).
        var res = await RequireQuery().Query(ScopedById(id, managed), ct);
        if (res.Items.Count == 0) return false;
        return await _inner.Delete(id, ct);
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        var managed = ManagedReadFilter();
        if (managed is null) return await _inner.DeleteMany(idList, ct);
        var res = await RequireQuery().Query(ScopedByIds(idList, managed), ct);
        var owned = res.Items.Select(e => e.Id).ToList();
        return owned.Count == 0 ? 0 : await _inner.DeleteMany(owned, ct);
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        if (managed is not null)
        {
            // NEVER the unscoped Clear instruction under a managed scope — it would wipe every scope's rows.
            var owned = await RequireQuery().Query(QueryDefinition.All.Where(managed), ct);
            return await _inner.DeleteMany(owned.Items.Select(e => e.Id), ct);
        }
        if (_inner is IInstructionExecutor<TEntity> exec)
        {
            try { return await exec.ExecuteAsync<int>(new Instruction(DataInstructions.Clear), ct); }
            catch (NotSupportedException) { /* fall back */ }
        }
        var all = await RequireQuery().Query(QueryDefinition.All, ct);
        return await _inner.DeleteMany(all.Items.Select(e => e.Id), ct);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ManagedReadFilter();
        if (managed is null) return await _inner.RemoveAll(strategy, ct);
        // RemoveAll is an unscoped truncate/wipe — never allowed to cross a managed boundary. Lower to a scoped delete.
        var owned = await RequireQuery().Query(QueryDefinition.All.Where(managed), ct);
        var ids = owned.Items.Select(e => e.Id).ToList();
        return ids.Count == 0 ? 0L : await _inner.DeleteMany(ids, ct);
    }

    // Forward the inner adapter's conditional compare-and-set (probe via DataCaps.Write.ConditionalReplace). Under an
    // active managed scope this fails closed: the compare-and-set guard is a CLR predicate over POCO properties and
    // cannot carry the managed (non-POCO) predicate, so a CAS could retarget a row in another scope. Use Upsert.
    public async Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
    {
        await Guard(ct);
        if (HasManaged && CurrentManagedValues() is not null)
            throw new NotSupportedException(
                $"ConditionalReplaceAsync is not supported for managed-field-scoped entity '{typeof(TEntity).Name}' under an " +
                "active managed scope — the compare-and-set guard cannot carry the managed predicate. Use Upsert (conflict-aware).");
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
            // Batch path applies the batch-eligible write-stamps (identity; NOT [Timestamp] — the shipped invariant).
            foreach (var e in _adds) { ct.ThrowIfCancellationRequested(); _outer._writePlan.ApplyBatch(e); }
            foreach (var e in _updates) { ct.ThrowIfCancellationRequested(); _outer._writePlan.ApplyBatch(e); }
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    ct.ThrowIfCancellationRequested();
                    // Managed-scoped load (IDOR): a mutate-by-id never loads (and so never re-stamps) another scope's row.
                    var current = await _outer.Get(id, ct);
                    if (current is not null)
                    {
                        mutate(current);
                        _outer._writePlan.ApplyBatch(current);
                        _updates.Add(current);
                    }
                }
            }

            var native = _outer._inner.CreateBatch();
            foreach (var e in _adds) native.Add(e);
            foreach (var e in _updates) native.Update(e);
            foreach (var id in _deletes) native.Delete(id);

            var values = _outer.CurrentManagedValues();
            if (values is null) return await native.Save(options, ct);
            // Stamp-AND-verify the whole batch under one scope (all rows share the ambient managed value).
            using (ManagedFieldWriteScope.Enter(values))
                return await native.Save(options, ct);
        }
    }
}
