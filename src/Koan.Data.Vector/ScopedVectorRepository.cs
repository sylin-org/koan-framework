using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Vector;

/// <summary>
/// GAP C 0.3 — the data-axis isolation chokepoint for the <b>vector</b> path (the STOR-0011 twin). Decorates an
/// <see cref="IVectorSearchRepository{TEntity,TKey}"/> so that EVERY write <b>stamps</b> the registered equality
/// axes (e.g. the tenant <c>__koan_tenant</c>) into the vector metadata, and EVERY search <b>ANDs</b> a
/// <c>__koan_tenant == &lt;ambient&gt;</c> predicate into the filter — so a KNN returns only the active scope's
/// vectors. Applied at <see cref="VectorService.TryGetRepository{TEntity,TKey}"/>, the one place every facade
/// (<c>Vector&lt;T&gt;</c>, <c>VectorData&lt;T&gt;</c>, and the direct-<c>Repo</c> writes) resolves the repository.
///
/// <para><b>Off = byte-identical:</b> when no managed field is registered (<c>ManagedFieldRegistry.IsEmpty</c>) or
/// none applies/has a value, metadata and filter pass through unchanged. <b>Fail-closed:</b> an adapter that does
/// not announce metadata filtering (<see cref="VectorCaps.Filters"/>) cannot enforce the scope predicate, so a
/// search under an active scope throws rather than leak (DATA-0105 §3.3). Axis-generic — it reads
/// <see cref="ManagedFieldRegistry"/>, never names "tenant".</para>
///
/// <para>v1 covers the leak-and-stamp surfaces (Upsert/UpsertMany/Search). <c>Delete</c>/<c>GetEmbedding</c> are
/// by-id with no filter slot (a delete-IDOR / read-IDOR follow-on); <c>Flush</c> is an admin op. A vector saved
/// with <b>non-dictionary</b> metadata under an active scope cannot be stamped and is therefore excluded from a
/// scoped search (safe not-found, not a leak) — use dictionary metadata under tenancy.</para>
/// </summary>
internal sealed class ScopedVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>, IDescribesCapabilities, IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IVectorSearchRepository<TEntity, TKey> _inner;
    private readonly bool _canFilter;

    public ScopedVectorRepository(IVectorSearchRepository<TEntity, TKey> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _canFilter = VectorCaps.Describe(inner, inner.GetType().Name).Has(VectorCaps.Filters);
    }

    public Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        RunGuards();
        return _inner.Upsert(id, embedding, StampScope(metadata), ct);
    }

    public Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        RunGuards();
        return _inner.UpsertMany(items.Select(x => (x.Id, x.Embedding, StampScope(x.Metadata))), ct);
    }

    public Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        RunGuards();
        var scope = ScopeFilter();
        if (scope is null) return _inner.Search(options, ct);          // off / [HostScoped] ⇒ unchanged (byte-identical)
        if (!_canFilter)
            throw new NotSupportedException(
                $"Vector search on a scoped entity '{typeof(TEntity).Name}' fails closed: adapter " +
                $"'{_inner.GetType().Name}' does not announce metadata filtering ({nameof(VectorCaps.Filters)}), so the " +
                $"scope predicate cannot be enforced (GAP C 0.3 / DATA-0105 §3.3).");
        var combined = options.Filter is null ? scope : Filter.All(options.Filter, scope);
        return _inner.Search(options with { Filter = combined }, ct);
    }

    // Pass-through (v1 follow-ons noted in the type summary).
    public Task<bool> Delete(TKey id, CancellationToken ct = default) => _inner.Delete(id, ct);
    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) => _inner.DeleteMany(ids, ct);
    public Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default) => _inner.GetEmbedding(id, ct);
    public Task<Dictionary<TKey, float[]>> GetEmbeddings(IEnumerable<TKey> ids, CancellationToken ct = default) => _inner.GetEmbeddings(ids, ct);
    public Task VectorEnsureCreated(CancellationToken ct = default) => _inner.VectorEnsureCreated(ct);
    public Task Flush(CancellationToken ct = default) => _inner.Flush(ct);
    public IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(int? batchSize = null, CancellationToken ct = default) => _inner.ExportAll(batchSize, ct);

    public void Describe(ICapabilities caps) => VectorCaps.Describe(_inner, _inner.GetType().Name).CopyInto(caps);

    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
        => _inner is IInstructionExecutor<TEntity> exec
            ? exec.ExecuteAsync<TResult>(instruction, ct)
            : throw new NotSupportedException($"Vector adapter '{_inner.GetType().Name}' does not support instructions.");

    // --- scope helpers (mirror ManagedEqualityReadContributor + the relational write-stamp; equality axes only) ---

    // Convergence (review wf_cc13952a-197): run the SAME fail-closed guard the data + storage paths use
    // (IStorageGuard / TenantStorageGuard). Without it a tenant-scoped vector op with NO tenant in scope fell
    // through to an UNFILTERED search / an unstamped write (a cross-tenant leak under prod-Closed posture). The
    // guard exempts [HostScoped] itself, fails closed under Closed, and warns under dev-Open. Off (no guard
    // registered) ⇒ no-op ⇒ byte-identical.
    private static void RunGuards()
    {
        var sp = AppHost.Current;
        if (sp is null) return;
        foreach (var g in sp.GetServices<IStorageGuard>()) g.Guard(typeof(TEntity));
    }

    private static object? StampScope(object? metadata)
    {
        if (ManagedFieldRegistry.IsEmpty) return metadata;
        if (!TryGetMutableDict(metadata, out var dict)) return metadata;   // non-dict ⇒ unstampable (excluded on read)
        var managed = ManagedFieldRegistry.ForType(typeof(TEntity));
        var stamped = false;
        for (var i = 0; i < managed.Count; i++)
        {
            var d = managed[i];
            if (!d.AutoReadFilter)                                        // §3 (mirroring STOR-0011): a non-equality axis
            {                                                            // is never a vector metadata segment...
                if (d.ValueProvider() is not null)                       // ...and a value-yielding one FAILS CLOSED
                    throw new InvalidOperationException(
                        $"Non-equality axis '{d.StorageName}' cannot scope a vector — the metadata path is equality-only (GAP C 0.3 / STOR-0011 §3).");
                continue;
            }
            var v = d.ValueProvider();
            if (v is null) continue;
            dict[d.StorageName] = v;
            stamped = true;
        }
        return stamped ? dict : metadata;
    }

    private static Filter? ScopeFilter()
    {
        if (ManagedFieldRegistry.IsEmpty) return null;
        var managed = ManagedFieldRegistry.ForType(typeof(TEntity));
        List<Filter>? preds = null;
        for (var i = 0; i < managed.Count; i++)
        {
            var d = managed[i];
            if (!d.AutoReadFilter)                                        // §3: a non-equality axis fails closed (never dropped)
            {
                if (d.ValueProvider() is not null)
                    throw new InvalidOperationException(
                        $"Non-equality axis '{d.StorageName}' cannot scope a vector search — the metadata filter is equality-only (GAP C 0.3 / STOR-0011 §3).");
                continue;
            }
            var v = d.ValueProvider();
            if (v is null) continue;
            (preds ??= new List<Filter>()).Add(Filter.Eq(d.StorageName, v));
        }
        return preds is null ? null : preds.Count == 1 ? preds[0] : Filter.All(preds.ToArray());
    }

    private static bool TryGetMutableDict(object? metadata, out Dictionary<string, object?> dict)
    {
        // Note: at runtime object? == object, so the <string,object?> cases also match a Dictionary<string,object>.
        switch (metadata)
        {
            case null: dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase); return true;
            case IDictionary<string, object?> d: dict = new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase); return true;
            case IReadOnlyDictionary<string, object?> d: dict = new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase); return true;
            default: dict = null!; return false;
        }
    }
}
