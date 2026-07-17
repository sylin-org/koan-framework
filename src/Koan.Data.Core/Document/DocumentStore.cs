using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Adapters;
using Koan.Data.Adapters;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.Document;

/// <summary>
/// The document storage-model family base (ARCH-0103 §5; the document-store catalogue). It owns — once for the whole
/// family — the cross-cutting shell every server-backed document adapter otherwise copy-pastes: the readiness-gated +
/// traced <b>operation template</b>, the <b>AODB managed-write composition</b> (Shared mode), the schema-ready gate,
/// the capability floor, and the batch / instruction skeletons. A concrete adapter (the golden reference: Mongo)
/// supplies only the thin <b>native primitives</b> below — its driver does the actual document work.
///
/// <para><b>The three AODB modes</b> are realized by composition: <b>Shared</b> — the base reads
/// <see cref="ManagedFieldWriteScope.Effective"/> (inject) / <see cref="ManagedFieldWriteScope.Current"/> (guard) and
/// hands both to <see cref="UpsertOneNativeAsync"/>, which stamps + conflict-guards the write natively; <b>Container</b>
/// — the dialect resolves a distinct native container per ambient partition; <b>Database</b> — the factory resolves a
/// distinct connection per routed source. The base declares <see cref="DataCaps.Isolation.RowScoped"/>; the dialect
/// declares its native extras (bulk / atomic / CAS / TTL).</para>
///
/// <para><b>Cross-cutting (ARCH-0103 evaluation):</b> the op-template gates readiness via the shared
/// <see cref="DataAdapterReadinessExtensions.WithDataReadinessAsync{T,TEntity}"/> (so schema auto-provision rides too) and opens
/// one <see cref="Activity"/> per op with consistent tags (entity · source · partition) and error status — the
/// telemetry + readiness boilerplate the per-adapter repos used to repeat ~12 times each.</para>
/// </summary>
public abstract class DocumentStore<TEntity, TKey> :
    AdapterReadinessForwardingBase,
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // ==================== The native seam (the dialect supplies these) ====================
    // (Readiness — the connection provider — and the readiness config Policy/Timeout/EnableReadinessGating are inherited
    // from AdapterReadinessForwardingBase; the dialect overrides them.)

    /// <summary>The provider's <see cref="ActivitySource"/> (e.g. <c>Koan.Data.Connector.Mongo</c>).</summary>
    protected abstract ActivitySource Telemetry { get; }

    /// <summary>The span-name prefix for this provider (e.g. <c>mongo</c>): each op opens <c>{Verb}.{op}</c>.</summary>
    protected abstract string Verb { get; }

    /// <summary>The routed source for the current repository (tagged on every span); <c>null</c> for the default source.</summary>
    protected virtual string? RoutedSource => null;

    /// <summary>Ensure the CURRENT (ambient-partition) container exists and is indexed. Invoked through <see cref="Schema"/>
    /// (once per container) by the dialect's container resolution, and directly by an explicit <c>EnsureCreated</c>.</summary>
    protected abstract Task EnsureContainerAsync(CancellationToken ct);

    protected abstract Task<TEntity?> FindByIdAsync(TKey id, CancellationToken ct);
    protected abstract Task<IReadOnlyList<TEntity?>> FindManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct);
    protected abstract Task<RepositoryQueryResult<TEntity>> QueryNativeAsync(QueryDefinition query, CancellationToken ct);
    protected abstract Task<CountResult> CountNativeAsync(QueryDefinition query, CancellationToken ct);

    /// <summary>Upsert one document, stamping <paramref name="inject"/> and conflict-guarding against
    /// <paramref name="guard"/> (both <c>null</c>/empty off any managed scope ⇒ the byte-identical plain-upsert path).</summary>
    protected abstract Task UpsertOneNativeAsync(TEntity model, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct);
    protected abstract Task<int> UpsertManyNativeAsync(IReadOnlyList<TEntity> models, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct);
    protected abstract Task<bool> DeleteOneNativeAsync(TKey id, CancellationToken ct);
    protected abstract Task<int> DeleteManyNativeAsync(IReadOnlyList<TKey> ids, CancellationToken ct);
    protected abstract Task<long> ClearNativeAsync(RemoveStrategy strategy, CancellationToken ct);
    protected abstract Task<BatchResult> SaveBatchNativeAsync(IReadOnlyList<TEntity> upserts, IReadOnlyList<TKey> deletes, bool requireAtomic, CancellationToken ct);

    /// <summary>Announce the backend's <see cref="DataCaps.Query.Filter"/> detail + native extras (bulk / atomic / CAS / TTL).
    /// The family floor (Linq + RowScoped) is added by <see cref="Describe"/>.</summary>
    protected abstract void DescribeBackend(ICapabilities caps);

    /// <summary>Map a <see cref="RemoveStrategy.Optimized"/> request to the backend's preferred concrete strategy.</summary>
    protected virtual RemoveStrategy ResolveStrategy(RemoveStrategy strategy) => strategy;

    /// <summary>The shared schema-ready gate (one run per container key). The dialect's container resolution drives it.</summary>
    protected OnceGate Schema { get; } = new();

    // (Readiness forwarding + Policy/Timeout/EnableReadinessGating live on AdapterReadinessForwardingBase.)

    // ==================== The operation template (readiness gate + tracing) ====================

    /// <summary>Run a native op under the readiness gate (which also rides schema auto-provisioning) and one traced
    /// span tagged with entity / source / partition, recording the error status on a throw. Observes cancellation
    /// eagerly (before any work) so a pre-cancelled token short-circuits even an op that would otherwise no-op.</summary>
    protected Task<T> RunAsync<T>(string op, Func<Task<T>> native, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return this.WithDataReadinessAsync<T, TEntity>(
            () => AdapterActivity.TraceAsync(Telemetry, $"{Verb}.{op}", Tag, native), ct);
    }

    /// <summary>Tag a backend-op span with the entity, routed source, and ambient partition.</summary>
    private void Tag(Activity activity)
    {
        activity.SetTag("db.entity", typeof(TEntity).FullName);
        if (RoutedSource is { Length: > 0 } source) activity.SetTag("koan.source", source);
        var partition = EntityContext.Current?.Partition;
        if (partition is { Length: > 0 }) activity.SetTag("koan.partition", partition);
    }

    // ==================== Read ====================

    public Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => RunAsync("get", () => FindByIdAsync(id, ct), ct);

    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => RunAsync("get.many", () => FindManyAsync(ids as IReadOnlyList<TKey> ?? ids.ToList(), ct), ct);

    public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
        => RunAsync("query", () => QueryNativeAsync(query, ct), ct);

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        => RunAsync("count", () => CountNativeAsync(query, ct), ct);

    // ==================== Write (Shared-mode managed composition) ====================

    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
        => RunAsync("upsert", async () =>
        {
            // Inject from Effective (isolation ∪ operation override, e.g. soft-delete __deleted); GUARD only on Current
            // (the isolation values) — the dialect realizes the conflict-aware write natively (ARCH-0101 §4).
            await UpsertOneNativeAsync(model, ManagedFieldWriteScope.Effective, ManagedFieldWriteScope.Current, ct).ConfigureAwait(false);
            return model;
        }, ct);

    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => RunAsync("upsert.many", () =>
        {
            var list = models as IReadOnlyList<TEntity> ?? models.ToList();
            return UpsertManyNativeAsync(list, ManagedFieldWriteScope.Effective, ManagedFieldWriteScope.Current, ct);
        }, ct);

    public Task<bool> Delete(TKey id, CancellationToken ct = default)
        => RunAsync("delete", () => DeleteOneNativeAsync(id, ct), ct);

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => RunAsync("delete.many", () => DeleteManyNativeAsync(ids as IReadOnlyList<TKey> ?? ids.ToList(), ct), ct);

    public Task<int> DeleteAll(CancellationToken ct = default)
        => RunAsync("delete.all", async () => (int)await ClearNativeAsync(RemoveStrategy.Safe, ct).ConfigureAwait(false), ct);

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => RunAsync("remove.all", () => ClearNativeAsync(ResolveStrategy(strategy), ct), ct);

    // ==================== Batch ====================

    public IBatchSet<TEntity, TKey> CreateBatch() => new DocBatch(this);

    // BATCH CONTRACT (family-uniform, mirrors KeyValueStore): a batch applies all upserts (adds + updates + replayed
    // mutations) BEFORE all deletes — not in interleaved call order. Mixing an upsert and a delete of the SAME id in one
    // batch is therefore undefined-by-contract (the upsert wins, the delete then removes it). Group by intent, don't
    // interleave same-id upsert/delete.
    private sealed class DocBatch : IBatchSet<TEntity, TKey>
    {
        private readonly DocumentStore<TEntity, TKey> _repo;
        private readonly List<TEntity> _upserts = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public DocBatch(DocumentStore<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _upserts.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _upserts.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _upserts.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            // Replay read-mutate-write mutations into the upsert set (through the gated Get).
            foreach (var (id, mutate) in _mutations)
            {
                var current = await _repo.Get(id, ct).ConfigureAwait(false);
                if (current is not null) { mutate(current); _upserts.Add(current); }
            }
            if (_upserts.Count == 0 && _deletes.Count == 0) return new BatchResult(0, 0, 0);
            return await _repo.RunAsync(
                "batch",
                () => _repo.SaveBatchNativeAsync(_upserts, _deletes, options?.RequireAtomic == true, ct),
                ct).ConfigureAwait(false);
        }
    }

    // ==================== Instructions ====================

    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        // Readiness-gated, but NOT through the schema-auto-provision path (EnsureCreated IS the provisioning — gating it
        // through the retry would recurse).
        return this.WithReadinessAsync<TResult>(async () =>
        {
            switch (instruction.Name)
            {
                case DataInstructions.EnsureCreated:
                    await EnsureContainerAsync(ct).ConfigureAwait(false);
                    return (TResult)(object)true;
                case DataInstructions.Clear:
                    return (TResult)(object)(int)await ClearNativeAsync(RemoveStrategy.Safe, ct).ConfigureAwait(false);
                default:
                    throw new NotSupportedException(
                        $"Instruction '{instruction.Name}' is not supported by the {GetType().Name} document adapter for {typeof(TEntity).Name}.");
            }
        }, ct);
    }

    // UNGATED by design: the facade calls EnsureReady before every op (RepositoryFacade.Guard), and EnsureContainerAsync
    // is what CONNECTS the provider (transitioning Initializing→Ready) and ensures the schema. Gating it on readiness
    // would deadlock — waiting for a Ready state only this call can produce. The dialect makes it idempotent (one run
    // per container via Schema).
    //
    // SCHEMA MEMOIZATION: the schema-ready state is memoized per container (the Schema gate) — ensure (container +
    // indexes) runs ONCE per container per process, then short-circuits. An out-of-band external drop of the container
    // is therefore not auto-re-ensured (a process restart or RemoveAll(Fast), which invalidates the gate, re-ensures);
    // in practice a write recreates the container at the driver level, so only declared indexes lag until then.
    public Task EnsureReady(CancellationToken ct = default) => EnsureContainerAsync(ct);

    // ==================== Capabilities ====================

    public void Describe(ICapabilities caps)
    {
        caps.Add(DataCaps.Query.Linq)
            // The AODB three-mode ledger (ARCH-0103 §6) — the family realizes all three uniformly: Shared stamps the
            // framework-managed discriminator + conflict-guards the write; Container resolves a distinct native container
            // per ambient partition (the dialect); Database routes per source (the factory). Co-defined with the
            // AodbConformanceSpecsBase cells that prove each.
            .Add(DataCaps.Isolation.RowScoped)
            .Add(DataCaps.Isolation.ContainerScoped)
            .Add(DataCaps.Isolation.DatabaseScoped);
        DescribeBackend(caps);
    }

    // ==================== Shared diagnostics ====================

    /// <summary>The generic cross-scope write rejection (ARCH-0101 — names the entity/id, never the tenant/axis).
    /// The dialect throws this when an existing document is owned by a different managed scope.</summary>
    protected static InvalidOperationException CrossScopeWrite(string container, object? id)
        => new($"Rejected a cross-scope write to '{container}' id '{id}': the document is owned by a different managed " +
               "scope (e.g. tenant/classification). A managed-field-scoped entity cannot overwrite another scope's document.");
}
