using System.Linq.Expressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Metadata;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Lifecycle;

namespace Koan.Data.Core;

/// <summary>
/// Adds cross-cutting behaviors on top of an underlying repository:
/// - Ensures identifiers for all upserts (single, many, batch)
/// - Auto-updates [Timestamp] fields on save operations
/// - Owns host-composed Entity Lifecycle around materialization, upsert, and removal
/// - Advertises query/write capabilities
/// - Bridges the structured query (<see cref="IQueryRepository{TEntity,TKey}"/>) and raw query
///   (<see cref="IRawQueryRepository{TEntity,TKey}"/>) surfaces of the inner adapter
/// - Forwards instruction execution when supported by the adapter
/// - Honours <b>managed fields</b> (DATA-0105 §3b) — the invisible framework-managed isolation discriminators a
///   cross-cutting module registers (e.g. Koan.Tenancy). The facade is the gateway for the repository path: it
///   stamps the managed value on writes (the inner adapter persists + verifies it), AND-folds a managed predicate
///   into reads, lowers key-ops to managed-scoped queries (IDOR), scopes RemoveAll/DeleteAll, and fails closed on
///   the paths the managed predicate cannot cover (raw / conditional-replace / a non-isolating adapter).
/// Provider/module decorators sit inside this facade. This is the one application-facing Data boundary,
/// so an inner cache hit or specialized provider path cannot bypass these semantics.
/// </summary>
internal sealed class RepositoryFacade<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IBoundedQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>,
    IAxisScopeDiagnostics
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly StorageWritePlan _writePlan;
    private readonly StorageFieldTransformPlan _fieldTransform;
    private readonly IStorageGuard[] _guards;
    private readonly IReadFilterContributor[] _readContributors;
    private readonly IReadOnlyList<ManagedFieldDescriptor> _managed;
    private readonly string _idField;
    private readonly bool _scopeAdapterOk;
    private readonly string? _scopeAdapterError;
    private readonly FilterSupport _filterCaps;
    private readonly bool _skipReadPushabilityCheck;
    private readonly OperationOverrideDescriptor? _deleteOverride;
    private readonly EntityLifecyclePlan<TEntity, TKey>? _lifecycle;

    public RepositoryFacade(
        IDataRepository<TEntity, TKey> inner,
        IStorageGuard[]? guards = null,
        IReadFilterContributor[]? readContributors = null,
        EntityLifecyclePlan<TEntity, TKey>? lifecycle = null)
    {
        _inner = inner;
        _guards = guards ?? Array.Empty<IStorageGuard>();
        _readContributors = readContributors ?? Array.Empty<IReadFilterContributor>();
        _writePlan = StorageWritePlan.For(typeof(TEntity));
        _fieldTransform = StorageFieldTransformPlan.For(typeof(TEntity));
        _managed = ManagedFieldRegistry.ForType(typeof(TEntity));
        _idField = AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop.Name ?? "Id";
        // The adapter is inspected once iff this entity could ever be scoped: it has a managed descriptor (write-stamp
        // + equality read) OR a NON-default read-filter contributor (a predicate axis). The built-in equality
        // contributor alone is not a trigger — with no managed descriptor it yields no filter, so a non-tenant app is
        // a true no-op (byte-identical: no DataCaps.Describe, no FilterSupport). The result is consulted only when a
        // scope is actually active at runtime (DATA-0106 §4 fail-closed deferral).
        var couldScope = _managed.Count > 0 || HasNonDefaultReadContributor();
        if (couldScope) (_scopeAdapterOk, _scopeAdapterError, _filterCaps) = InspectScopeAdapter();
        else { _scopeAdapterOk = true; _filterCaps = FilterSupport.None; }
        // Hot-path ([koan-design-principles] §2/§4): the equality (tenancy) read-filter shape is static per (type,adapter)
        // — Eq over a fixed managed field — so its pushability is a CONSTANT, proven ONCE here. When the only active read
        // scope is the built-in equality contributor (no predicate axis) and that shape is pushable, the per-read Split is
        // skipped (RequireScopeForRead degrades to the single bool check — byte-identical to the pre-DATA-0106 read cost).
        // A predicate axis (dynamic shape) keeps the per-read Split as the source of truth.
        _skipReadPushabilityCheck = couldScope && _scopeAdapterOk
            && !HasNonDefaultReadContributor() && EqualityShapeIsPushable();
        // The delete operation-override (soft-delete) is a per-(type) constant — registered boot-time, AppliesTo is a
        // static predicate — so resolve it ONCE here (mirrors _managed), not per delete (no per-op registry lock).
        _deleteOverride = OperationOverrideRegistry.ForDelete(typeof(TEntity));
        _lifecycle = lifecycle;
    }

    private bool HasNonDefaultReadContributor()
    {
        for (var i = 0; i < _readContributors.Length; i++)
            if (_readContributors[i] is not ManagedEqualityReadContributor) return true;
        return false;
    }

    // The built-in equality contributor only ever emits Filter.Eq(StorageName, value) for AutoReadFilter descriptors.
    // Pushability of an Eq depends only on (field, operator) — never the value — so it is a per-(type,adapter) constant
    // we can settle once at construction and skip on every subsequent read (fix for the per-read Split regression).
    private bool EqualityShapeIsPushable()
    {
        foreach (var d in _managed)
        {
            if (!d.AutoReadFilter) continue;
            var probe = Filter.Eq(d.StorageName, "_");   // the value is irrelevant to pushability
            if (FilterSplitter.Split(probe, _filterCaps, typeof(TEntity)).Residual is not null) return false;
        }
        return true;
    }

    // --- field-transform helpers (ARCH-0098 §0). All are no-op fast paths when the type has no transform. ---

    /// <summary>The persist payload for a write: an encrypted clone when a transform exists, else the entity itself.</summary>
    private TEntity WritePayload(TEntity entity)
        => _fieldTransform.HasTransforms ? (TEntity)_fieldTransform.CloneForWrite(entity) : entity;

    /// <summary>Restore plaintext on a single returned entity, in place.</summary>
    private TEntity? Reverse(TEntity? entity)
    {
        if (entity is not null && _fieldTransform.HasTransforms) _fieldTransform.ApplyOnRead(entity);
        return entity;
    }

    /// <summary>Restore plaintext on every entity in a query result, in place.</summary>
    private RepositoryQueryResult<TEntity> Reverse(RepositoryQueryResult<TEntity> result)
    {
        if (_fieldTransform.HasTransforms)
            for (var i = 0; i < result.Items.Count; i++)
            {
                var e = result.Items[i];
                if (e is not null) _fieldTransform.ApplyOnRead(e);
            }
        return result;
    }

    /// <summary>Restore plaintext on every non-null entity in a get-many result, in place.</summary>
    private IReadOnlyList<TEntity?> Reverse(IReadOnlyList<TEntity?> items)
    {
        if (_fieldTransform.HasTransforms)
            for (var i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e is not null) _fieldTransform.ApplyOnRead(e);
            }
        return items;
    }

    /// <summary>Persist an Upsert under the current managed write scope (the shared tenant/managed-field path).</summary>
    private async Task<TEntity> PersistUpsert(TEntity payload, CancellationToken ct)
    {
        var values = CurrentManagedValues();
        if (values is null) return await _inner.Upsert(payload, ct);
        using (ManagedFieldWriteScope.Enter(values)) return await _inner.Upsert(payload, ct);
    }

    private bool HasManaged => _managed.Count > 0;

    // Inspect (do NOT throw at construction) whether the adapter can isolate a scoped entity. It must announce every
    // required isolation capability — over BOTH the managed descriptors (write-stamp + equality read) AND the
    // read-filter contributors (a predicate axis declares its own) — AND be an IQueryRepository (key-ops lower to
    // scoped queries). The adapter's FilterSupport is captured for the per-read pushability check (§4b). We defer the
    // throw to the first operation that actually has a scope in effect, so a non-tenant app — or a referenced-but-off
    // axis on a non-isolating adapter — is a true no-op (zero regression). Fail-closed when used.
    private (bool ok, string? error, FilterSupport caps) InspectScopeAdapter()
    {
        var describe = DataCaps.Describe(_inner, _inner.GetType().Name);
        var caps = describe.Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        foreach (var d in _managed)
        {
            if (d.RequiredCapability is { } req && !describe.Has(req))
                return (false,
                    $"Entity '{typeof(TEntity).Name}' is in an active managed scope requiring isolation capability '{req.Id}', " +
                    $"but the adapter '{_inner.GetType().Name}' does not announce it. Route it to an isolating adapter, or exempt the entity.",
                    caps);
        }
        foreach (var c in _readContributors)
        {
            if (c.RequiredCapability is { } req && !describe.Has(req))
                return (false,
                    $"Entity '{typeof(TEntity).Name}' is read-scoped by a contributor requiring isolation capability '{req.Id}', " +
                    $"but the adapter '{_inner.GetType().Name}' does not announce it. Route it to an isolating adapter, or exempt the entity.",
                    caps);
        }
        if (_inner is not IQueryRepository<TEntity, TKey>)
            return (false,
                $"Entity '{typeof(TEntity).Name}' is in an active managed scope, but the adapter '{_inner.GetType().Name}' does not " +
                "implement IQueryRepository. Managed isolation lowers key operations to scoped queries, so it requires pushdown query support.",
                caps);
        return (true, null, caps);
    }

    /// <summary>Fail-closed for a scoped WRITE: the adapter must satisfy the static isolation contract (capability + query).</summary>
    private void RequireScopeStatic()
    {
        if (!_scopeAdapterOk) throw new InvalidOperationException(_scopeAdapterError);
    }

    /// <summary>
    /// Fail-closed for a scoped READ: the static contract PLUS the folded predicate must be <b>fully pushable</b> by the
    /// adapter (DATA-0106 §4b). An isolation filter MUST be enforced at the store — a residual would fetch cross-scope
    /// rows into process memory and skew Count, which is itself a leak. Bias-to-strict: a contributor that yields a
    /// filter the adapter cannot push fails closed even if it declared no capability (a null capability is no free pass).
    /// </summary>
    private void RequireScopeForRead(Filter folded)
    {
        RequireScopeStatic();
        if (_skipReadPushabilityCheck) return;   // equality-only shape: pushability proven once at construction
        var split = FilterSplitter.Split(folded, _filterCaps, typeof(TEntity));
        if (split.Residual is not null)
            throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' is read-scoped by a predicate the adapter '{_inner.GetType().Name}' cannot fully " +
                "push down. An isolation filter must be enforced at the store, never evaluated in memory (a residual would fetch " +
                "cross-scope rows and skew Count). Route it to an adapter that pushes the predicate, or narrow the axis to a pushable shape.");
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
        if (values is not null) RequireScopeStatic();   // an active scope on a non-isolating adapter fails closed
        return values;
    }

    // --- operation-semantics override (ARCH-0101 §4) — the soft-delete plane ---

    /// <summary>The delete override for a single keyed delete, or <c>null</c> when none is registered OR this exact
    /// entity is being hard-deleted (a target-scoped <c>.HardDelete()</c> bypass). The mass-delete paths use
    /// <see cref="_deleteOverride"/> directly — there is no batch hard-delete, so they always apply the override.
    /// Either way the read-scoping (IDOR) below is RETAINED — the bypass is plane-specific.</summary>
    private OperationOverrideDescriptor? DeleteOverrideFor(TKey id)
        => _deleteOverride is not null && !OperationOverrideBypass.IsBypassedFor(typeof(TEntity), id)
            ? _deleteOverride
            : null;

    /// <summary>
    /// Re-persist a VISIBLE (already read-scoped) row with the override's state field set (e.g. <c>__deleted=true</c>),
    /// through the UNGUARDED operation-override write channel so the mutable field is injected but never conflict-guarded.
    /// The isolation stamp (tenant) stays GUARDED. The row round-trips through the field transform (plaintext → re-encrypt)
    /// only when one exists (else both are no-ops).
    /// </summary>
    private async Task OverrideUpsert(TEntity row, OperationOverrideDescriptor ov, CancellationToken ct)
    {
        Reverse(row);                       // ensure plaintext before re-encrypting on write (no-op without a transform)
        _writePlan.ApplyAll(row);           // identity + [Timestamp(OnSave)] — the override IS a write
        var overrides = new Dictionary<string, object?>(StringComparer.Ordinal) { [ov.Field] = ov.OnDeleteValue };
        var values = CurrentManagedValues();
        var payload = WritePayload(row);
        if (values is null)
            using (ManagedFieldWriteScope.EnterOverrides(overrides)) await _inner.Upsert(payload, ct);
        else
            using (ManagedFieldWriteScope.Enter(values, overrides)) await _inner.Upsert(payload, ct);
    }

    /// <summary>
    /// The AND-fold of every registered read-filter contributor's predicate isolating reads to the current ambient
    /// scope, or <c>null</c> when none constrains it (off / host / no axis) — DATA-0106 §2. Equality (tenancy) flows
    /// through the built-in <see cref="ManagedEqualityReadContributor"/>; a predicate axis (moderation) contributes its
    /// own. The tri-state is byte-identical to the former bespoke <c>ManagedReadFilter</c>: zero survivors ⇒ null (the
    /// unfiltered fast path); one ⇒ that filter; many ⇒ <c>Filter.All(survivors)</c> — no 1-element AllOf, no null operand.
    /// </summary>
    private Filter? ReadScopeFilter()
    {
        var folded = FoldReadScope();
        if (folded is null) return null;
        RequireScopeForRead(folded);                     // active scope on a non-isolating / non-pushing adapter fails closed
        return folded;
    }

    /// <summary>The AND-fold of every contributor's predicate in the current ambient, or <c>null</c> when nothing
    /// scopes now — WITHOUT the fail-closed throw (shared by <see cref="ReadScopeFilter"/> and the §9 diagnostics).</summary>
    private Filter? FoldReadScope() => ReadScopeFold.Fold(_readContributors, typeof(TEntity));

    // --- IAxisScopeDiagnostics (ARCH-0101 §8/§9): the non-throwing read-scope inspection DataAxis.Explain + the boot
    // pre-flight read. The facade is the ONE authority — it already computed _scopeAdapterOk / _filterCaps at construction. ---
    string IAxisScopeDiagnostics.AdapterName => _inner.GetType().Name;
    bool IAxisScopeDiagnostics.CouldScope => _managed.Count > 0 || HasNonDefaultReadContributor();
    bool IAxisScopeDiagnostics.ScopeAdapterOk => _scopeAdapterOk;
    string? IAxisScopeDiagnostics.ScopeAdapterError => _scopeAdapterOk ? null : _scopeAdapterError;
    Filter? IAxisScopeDiagnostics.CurrentReadScope() => FoldReadScope();
    bool IAxisScopeDiagnostics.IsFullyPushable(Filter folded)
        => _skipReadPushabilityCheck || FilterSplitter.Split(folded, _filterCaps, typeof(TEntity)).Residual is null;

    /// <summary>
    /// Whether any registered read-filter contributor constrains the current read of this type (ambient-active) — DATA-0106 §4.
    /// The raw-query and conditional-replace paths cannot carry the isolation predicate, so they fail closed when this is
    /// true. This trips for a PURE predicate axis (no managed field) too, which <c>CurrentManagedValues()</c> alone misses.
    /// </summary>
    private bool IsReadScoped()
    {
        for (var i = 0; i < _readContributors.Length; i++)
            if (_readContributors[i].ReadFilter(typeof(TEntity)) is not null) return true;
        return false;
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

    // Raw facade reads used by lifecycle Prior/remove preparation. They retain every Data-owned
    // guard, isolation, and field-transform decision already established by the caller, but do not
    // recursively emit a Load lifecycle while another lifecycle operation is in progress.
    private async Task<TEntity?> ReadOne(TKey id, CancellationToken ct)
    {
        var managed = ReadScopeFilter();
        if (managed is null) return Reverse(await _inner.Get(id, ct));
        var result = await RequireQuery().Query(ScopedById(id, managed), ct);
        return Reverse(result.Items.Count > 0 ? result.Items[0] : null);
    }

    private async Task<IReadOnlyList<TEntity?>> ReadMany(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        var managed = ReadScopeFilter();
        if (managed is null) return Reverse(await _inner.GetMany(ids, ct));
        var result = await RequireQuery().Query(ScopedByIds(ids, managed), ct);
        var byId = new Dictionary<TKey, TEntity>();
        foreach (var entity in result.Items) byId[entity.Id] = entity;
        return Reverse(ids.Select(id => byId.TryGetValue(id, out var entity) ? entity : null).ToList());
    }

    private async Task ApplyLoadLifecycle(IReadOnlyList<TEntity> entities, CancellationToken ct)
    {
        if (_lifecycle is not { HasLoad: true }) return;
        foreach (var entity in entities)
        {
            ct.ThrowIfCancellationRequested();
            await _lifecycle.ApplyLoad(entity, ct);
        }
    }

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        var entity = await ReadOne(id, ct);
        if (entity is not null && _lifecycle is { HasLoad: true })
            await _lifecycle.ApplyLoad(entity, ct);
        return entity;
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        var entities = await ReadMany(idList, ct);
        if (_lifecycle is { HasLoad: true })
            foreach (var entity in entities)
                if (entity is not null) await _lifecycle.ApplyLoad(entity, ct);
        return entities;
    }

    // --- structured query ---

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ReadScopeFilter();
        var result = Reverse(await RequireQuery().Query(managed is null ? query : ApplyManaged(query, managed), ct));
        await ApplyLoadLifecycle(result.Items, ct);
        return result;
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ReadScopeFilter();
        return await RequireQuery().Count(managed is null ? query : ApplyManaged(query, managed), ct);
    }

    public async Task<BoundedQueryResult<TEntity>> QueryBoundedCandidates(
        QueryDefinition query,
        int maxCandidates,
        CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ReadScopeFilter();
        var bounded = _inner as IBoundedQueryRepository<TEntity, TKey>
            ?? throw new NotSupportedException(
                $"The adapter backing {typeof(TEntity).Name} does not support provider-enforced bounded candidate reads.");
        var result = await bounded.QueryBoundedCandidates(
            managed is null ? query : ApplyManaged(query, managed),
            maxCandidates,
            ct);
        if (_fieldTransform.HasTransforms)
        {
            foreach (var entity in result.Items)
                _fieldTransform.ApplyOnRead(entity);
        }
        await ApplyLoadLifecycle(result.Items, ct);
        return result;
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
        GuardRawAgainstActiveScope();
        if (_inner is not IRawQueryRepository<TEntity, TKey> raw)
            throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
        var result = Reverse(await raw.QueryRaw(query, parameters, shaping, ct));
        await ApplyLoadLifecycle(result.Items, ct);
        return result;
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        await Guard(ct);
        GuardRawAgainstActiveScope();
        return _inner is IRawQueryRepository<TEntity, TKey> raw
            ? await raw.CountRaw(query, parameters, ct)
            : throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support raw queries.");
    }

    // The opaque raw SQL cannot carry the isolation predicate — neither the managed equality NOR a predicate axis's
    // (moderation) read-filter. So a raw read fails closed when EITHER an active managed write-scope OR any active
    // read-filter contributor constrains the type (DATA-0106 §4 — the trigger rides the contributor union, not just
    // _managed.Count; a pure predicate axis has no managed field and would otherwise slip through). RLS is the backstop.
    private void GuardRawAgainstActiveScope()
    {
        if (IsReadScoped() || (HasManaged && CurrentManagedValues() is not null))
            throw new NotSupportedException(
                $"Raw queries on scoped entity '{typeof(TEntity).Name}' are not isolated by the read-filter " +
                "predicate (the SQL is opaque). They are gated behind a store-level isolation backstop (e.g. RLS); with " +
                "none available the call fails closed under an active scope rather than read across scopes.");
    }

    // --- writes ---

    private ValueTask<TEntity?> ReadPrior(TEntity entity, CancellationToken ct)
        => EqualityComparer<TKey>.Default.Equals(entity.Id, default!)
            ? new ValueTask<TEntity?>((TEntity?)null)
            : new ValueTask<TEntity?>(ReadOne(entity.Id, ct));

    private async Task<TEntity> PersistPreparedUpsert(TEntity model, CancellationToken ct)
    {
        _writePlan.ApplyAll(model);
        if (!_fieldTransform.HasTransforms) return await PersistUpsert(model, ct);
        await PersistUpsert(WritePayload(model), ct);
        return model;
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_lifecycle is not { HasUpsert: true })
            return await PersistPreparedUpsert(model, ct);

        var context = await _lifecycle.BeginUpsert(model, token => ReadPrior(model, token), ct);
        var persisted = await PersistPreparedUpsert(context.Current, ct);
        await _lifecycle.CompleteUpsert(context, persisted);
        return context.Current;
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        await Guard(ct);
        var list = models as IList<TEntity> ?? models.ToList();
        if (_lifecycle is { HasUpsert: true })
        {
            // Prepare the complete set before the first write so a domain rejection never creates a
            // framework-induced partial batch. Provider failures retain their normal non-transactional semantics.
            var contexts = new List<EntityLifecycleContext<TEntity>>(list.Count);
            foreach (var model in list)
            {
                ct.ThrowIfCancellationRequested();
                contexts.Add(await _lifecycle.BeginUpsert(model, token => ReadPrior(model, token), ct));
            }

            var persisted = new List<TEntity>(contexts.Count);
            foreach (var context in contexts)
                persisted.Add(await PersistPreparedUpsert(context.Current, ct));
            for (var i = 0; i < contexts.Count; i++)
                await _lifecycle.CompleteUpsert(contexts[i], persisted[i]);
            return persisted.Count;
        }

        foreach (var m in list)
        {
            ct.ThrowIfCancellationRequested();
            _writePlan.ApplyAll(m);
        }
        var payloads = _fieldTransform.HasTransforms
            ? (IList<TEntity>)list.Select(WritePayload).ToList()   // encrypted clones; the caller's list stays plaintext
            : list;
        var values = CurrentManagedValues();
        if (values is null) return await _inner.UpsertMany(payloads, ct);
        using (ManagedFieldWriteScope.Enter(values))
            return await _inner.UpsertMany(payloads, ct);
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await Guard(ct);
        if (_lifecycle is not { HasRemove: true })
            return await DeleteWithoutLifecycle(id, ct);

        var entity = await ReadOne(id, ct);
        if (entity is null) return false;
        var context = await _lifecycle.BeginRemove(entity, ct);
        var removed = await DeleteWithoutLifecycle(context.Current.Id, ct);
        if (removed) await _lifecycle.CompleteRemove(context);
        return removed;
    }

    private async Task<bool> DeleteWithoutLifecycle(TKey id, CancellationToken ct)
    {
        var managed = ReadScopeFilter();
        var ov = DeleteOverrideFor(id);   // null when this exact entity is being hard-deleted (target-scoped bypass)
        if (ov is not null)
        {
            // Soft-delete: load the VISIBLE (read-scoped) row, re-persist with the override field set. The load is
            // still IDOR-scoped, so a soft-delete can only soft-remove a row the caller can see.
            var scoped = managed is null ? QueryDefinition.All.Where(Filter.Eq(_idField, id)) : ScopedById(id, managed);
            var res = await RequireQuery().Query(scoped, ct);
            if (res.Items.Count == 0) return false;
            await OverrideUpsert(res.Items[0], ov, ct);
            return true;
        }
        if (managed is null) return await _inner.Delete(id, ct);
        // check-then-delete: only an owned row may be deleted by id (IDOR).
        var res2 = await RequireQuery().Query(ScopedById(id, managed), ct);
        if (res2.Items.Count == 0) return false;
        return await _inner.Delete(id, ct);
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        await Guard(ct);
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        if (_lifecycle is { HasRemove: true })
            return await DeleteManyWithLifecycle(idList, ct);
        return await DeleteManyWithoutLifecycle(idList, ct);
    }

    private async Task<int> DeleteManyWithLifecycle(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        var existing = await ReadMany(ids, ct);
        var contexts = new List<EntityLifecycleContext<TEntity>>(existing.Count);
        foreach (var entity in existing)
        {
            if (entity is null) continue;
            contexts.Add(await _lifecycle!.BeginRemove(entity, ct));
        }

        var completed = new List<EntityLifecycleContext<TEntity>>(contexts.Count);
        foreach (var context in contexts)
            if (await DeleteWithoutLifecycle(context.Current.Id, ct)) completed.Add(context);
        foreach (var context in completed)
            await _lifecycle!.CompleteRemove(context);
        return completed.Count;
    }

    private async Task<int> DeleteManyWithoutLifecycle(IReadOnlyList<TKey> idList, CancellationToken ct)
    {
        var managed = ReadScopeFilter();
        var ov = _deleteOverride;   // mass delete: no batch hard-delete exists, so the override always applies
        if (ov is not null)
        {
            var scoped = managed is null
                ? QueryDefinition.All.Where(Filter.In(_idField, idList.Cast<object?>().ToList()))
                : ScopedByIds(idList, managed);
            var soft = await RequireQuery().Query(scoped, ct);
            foreach (var row in soft.Items) await OverrideUpsert(row, ov, ct);
            return soft.Items.Count;
        }
        if (managed is null) return await _inner.DeleteMany(idList, ct);
        var res = await RequireQuery().Query(ScopedByIds(idList, managed), ct);
        var owned = res.Items.Select(e => e.Id).ToList();
        return owned.Count == 0 ? 0 : await _inner.DeleteMany(owned, ct);
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await Guard(ct);
        var managed = ReadScopeFilter();
        if (_lifecycle is { HasRemove: true })
        {
            var query = managed is null ? QueryDefinition.All : QueryDefinition.All.Where(managed);
            var visible = await RequireQuery().Query(query, ct);
            return await DeleteManyWithLifecycle(visible.Items.Select(entity => entity.Id).ToList(), ct);
        }
        var ov = _deleteOverride;   // mass delete: no batch hard-delete exists, so the override always applies
        if (ov is not null)
        {
            // Soft-delete-all: load the visible (read-scoped) rows and soft-remove each. Already-deleted rows are
            // outside the hide-deleted read scope, so they are not re-touched (idempotent).
            var q = managed is null ? QueryDefinition.All : QueryDefinition.All.Where(managed);
            var soft = await RequireQuery().Query(q, ct);
            foreach (var row in soft.Items) await OverrideUpsert(row, ov, ct);
            return soft.Items.Count;
        }
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
        var managed = ReadScopeFilter();
        if (_lifecycle is { HasRemove: true } && strategy != RemoveStrategy.Fast)
        {
            var query = managed is null ? QueryDefinition.All : QueryDefinition.All.Where(managed);
            var visible = await RequireQuery().Query(query, ct);
            return await DeleteManyWithLifecycle(visible.Items.Select(entity => entity.Id).ToList(), ct);
        }
        var ov = _deleteOverride;   // mass delete: no batch hard-delete exists, so the override always applies
        if (ov is not null)
        {
            // Soft-delete: RemoveAll is a SCOPED soft-remove of the visible rows, never a physical truncate.
            var q = managed is null ? QueryDefinition.All : QueryDefinition.All.Where(managed);
            var soft = await RequireQuery().Query(q, ct);
            foreach (var row in soft.Items) await OverrideUpsert(row, ov, ct);
            return soft.Items.Count;
        }
        if (managed is null) return await _inner.RemoveAll(strategy, ct);
        // RemoveAll is an unscoped truncate/wipe — never allowed to cross a managed boundary. Lower to a scoped delete.
        var owned = await RequireQuery().Query(QueryDefinition.All.Where(managed), ct);
        var ids = owned.Items.Select(e => e.Id).ToList();
        return ids.Count == 0 ? 0L : await _inner.DeleteMany(ids, ct);
    }

    // Forward the inner adapter's conditional compare-and-set (probe via DataCaps.Write.ConditionalReplace). Under an
    // active scope this fails closed: the compare-and-set guard is a CLR predicate over POCO properties and cannot carry
    // the isolation predicate (the managed equality OR a predicate axis's read-filter), so a CAS could retarget a row in
    // another scope. The trigger rides the contributor union (a pure predicate axis has no managed field). Use Upsert.
    public async Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
    {
        await Guard(ct);
        if (IsReadScoped() || (HasManaged && CurrentManagedValues() is not null))
            throw new NotSupportedException(
                $"ConditionalReplaceAsync is not supported for scoped entity '{typeof(TEntity).Name}' under an " +
                "active scope — the compare-and-set guard cannot carry the isolation predicate. Use Upsert (conflict-aware).");
        if (_inner is not IConditionalWriteRepository<TEntity, TKey> cas)
            throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} does not support conditional replace.");

        EntityLifecycleContext<TEntity>? context = null;
        if (_lifecycle is { HasUpsert: true })
            context = await _lifecycle.BeginUpsert(model, token => ReadPrior(model, token), ct);

        var current = context?.Current ?? model;
        _writePlan.ApplyAll(current);
        // Field transform (ARCH-0098 Blocker 2): persist an encrypted clone so a CAS write never stores plaintext.
        // A classified property must NOT appear in the guard (it compares stored ciphertext to caller plaintext).
        var replaced = await cas.ConditionalReplaceAsync(WritePayload(current), guard, ct);
        if (replaced && context is not null)
            await _lifecycle!.CompleteUpsert(context, current);
        return replaced;
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
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    ct.ThrowIfCancellationRequested();
                    // Managed-scoped load (IDOR): a mutate-by-id never loads (and so never re-stamps) another scope's row.
                    var current = await _outer.ReadOne(id, ct);
                    if (current is not null)
                    {
                        mutate(current);
                        _updates.Add(current);
                    }
                }
            }

            var addContexts = new List<EntityLifecycleContext<TEntity>>(_adds.Count);
            var updateContexts = new List<EntityLifecycleContext<TEntity>>(_updates.Count);
            if (_outer._lifecycle is { HasUpsert: true })
            {
                foreach (var entity in _adds)
                    addContexts.Add(await _outer._lifecycle.BeginUpsert(entity, token => _outer.ReadPrior(entity, token), ct));
                foreach (var entity in _updates)
                    updateContexts.Add(await _outer._lifecycle.BeginUpsert(entity, token => _outer.ReadPrior(entity, token), ct));
            }

            var removeContexts = new List<EntityLifecycleContext<TEntity>>(_deletes.Count);
            if (_outer._lifecycle is { HasRemove: true })
            {
                foreach (var id in _deletes)
                {
                    var entity = await _outer.ReadOne(id, ct);
                    if (entity is not null)
                        removeContexts.Add(await _outer._lifecycle.BeginRemove(entity, ct));
                }
            }

            var adds = addContexts.Count == 0 ? _adds : addContexts.Select(context => context.Current).ToList();
            var updates = updateContexts.Count == 0 ? _updates : updateContexts.Select(context => context.Current).ToList();
            var deletes = removeContexts.Count == 0 ? _deletes : removeContexts.Select(context => context.Current.Id).ToList();

            // Batch path applies the batch-eligible write-stamps (identity; NOT [Timestamp] — the shipped invariant).
            foreach (var entity in adds) { ct.ThrowIfCancellationRequested(); _outer._writePlan.ApplyBatch(entity); }
            foreach (var entity in updates) { ct.ThrowIfCancellationRequested(); _outer._writePlan.ApplyBatch(entity); }

            var native = _outer._inner.CreateBatch();
            // Field transform (ARCH-0098 Blocker 1): the native batch persists encrypted CLONES, so a batch write —
            // unlike a [Timestamp] stamp — never lands plaintext at rest. The caller's add/update instances stay plaintext.
            foreach (var entity in adds) native.Add(_outer.WritePayload(entity));
            foreach (var entity in updates) native.Update(_outer.WritePayload(entity));

            // A soft remove is an update, not a native delete. Lower it through the canonical remove path so
            // operation overrides, managed scope and Lifecycle retain exactly the same meaning as Entity.Remove().
            // This necessarily spans multiple writes; fail closed when the caller explicitly requires atomicity.
            var lowerSoftDeletes = _outer._deleteOverride is not null && deletes.Count != 0;
            if (lowerSoftDeletes && options?.RequireAtomic == true)
                throw new NotSupportedException(
                    $"Atomic batch removal is not available for soft-deleted entity '{typeof(TEntity).Name}'. " +
                    "Use a non-atomic batch or an adapter transaction that explicitly composes the updates.");
            if (!lowerSoftDeletes)
                foreach (var id in deletes) native.Delete(id);

            var values = _outer.CurrentManagedValues();
            BatchResult result;
            if (values is null) result = await native.Save(options, ct);
            else
            {
                // Stamp-AND-verify the whole batch under one scope (all rows share the ambient managed value).
                using (ManagedFieldWriteScope.Enter(values))
                    result = await native.Save(options, ct);
            }

            var completedRemoves = removeContexts;
            if (lowerSoftDeletes)
            {
                completedRemoves = new List<EntityLifecycleContext<TEntity>>(removeContexts.Count);
                if (removeContexts.Count != 0)
                {
                    foreach (var context in removeContexts)
                        if (await _outer.DeleteWithoutLifecycle(context.Current.Id, ct))
                            completedRemoves.Add(context);
                }
                else
                {
                    var deleted = 0;
                    foreach (var id in deletes)
                        if (await _outer.DeleteWithoutLifecycle(id, ct))
                            deleted++;
                    result = new BatchResult(result.Added, result.Updated, deleted);
                }

                if (removeContexts.Count != 0)
                    result = new BatchResult(result.Added, result.Updated, completedRemoves.Count);
            }

            if (_outer._lifecycle is { } lifecycle)
            {
                for (var i = 0; i < addContexts.Count; i++)
                    await lifecycle.CompleteUpsert(addContexts[i], adds[i]);
                for (var i = 0; i < updateContexts.Count; i++)
                    await lifecycle.CompleteUpsert(updateContexts[i], updates[i]);
                foreach (var context in completedRemoves)
                    await lifecycle.CompleteRemove(context);
            }

            return result;
        }
    }
}
