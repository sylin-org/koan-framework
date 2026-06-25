using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
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
/// GAP C 0.3 — the data-axis isolation chokepoint for the <b>vector</b> path. Decorates an
/// <see cref="IVectorSearchRepository{TEntity,TKey}"/> and is <b>completely contributor-agnostic</b>: it reuses the
/// EXACT same registered seams the data path uses, never re-deriving any axis logic of its own.
/// <list type="bullet">
/// <item><b>Write</b> (Upsert/UpsertMany): stamps every applicable <c>ManagedFieldDescriptor</c>'s value into the
/// vector metadata (the registered write-stamp contributor — equality AND non-equality fields, so a read predicate's
/// fields are present to filter on).</item>
/// <item><b>Search</b>: ANDs the <see cref="ReadScopeFold"/> of every registered <see cref="IReadFilterContributor"/>
/// (the ONE read-scope fold the facade read path uses) into the query — so tenancy's equality AND a future
/// moderation axis's non-equality predicate both scope a KNN, with no hand-rolled predicate here.</item>
/// <item><b>Guard</b>: runs every registered <see cref="IStorageGuard"/> (the same fail-closed gate data + storage
/// use) — so an op with no scope in an active axis fails closed rather than reading/writing unscoped.</item>
/// </list>
/// Applied at <see cref="VectorService.TryGetRepository{TEntity,TKey}"/>, the one place every facade
/// (<c>Vector&lt;T&gt;</c>, <c>VectorData&lt;T&gt;</c>, and the direct-<c>Repo</c> writes) resolves the repository.
/// Off (no contributors / no managed field / no guard registered) ⇒ pass-through (byte-identical). Fail-closed when
/// the fold yields a predicate but the adapter does not announce metadata filtering (<see cref="VectorCaps.Filters"/>).
///
/// <para>v1 follow-ons (noted, not yet closed): <c>Delete</c>/<c>GetEmbedding</c> by-id (no filter slot — IDOR);
/// <c>ExportAll</c>/<c>Flush</c> admin ops; non-dictionary metadata cannot be stamped and is excluded on read (safe).</para>
/// </summary>
internal sealed class ScopedVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>, IDescribesCapabilities, IInstructionExecutor<TEntity>, IDecoratedVectorRepository
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public object InnerRepository => _inner;

    private readonly IVectorSearchRepository<TEntity, TKey> _inner;
    private readonly IReadFilterContributor[] _readContributors;
    private readonly IStorageGuard[] _guards;
    private readonly bool _canFilter;

    public ScopedVectorRepository(IVectorSearchRepository<TEntity, TKey> inner, IServiceProvider sp)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // Resolve the SAME registered contributor seams the data path consumes (boot-stable singletons).
        _readContributors = sp.GetServices<IReadFilterContributor>().ToArray();
        _guards = sp.GetServices<IStorageGuard>().ToArray();
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
        var scope = ReadScopeFold.Fold(_readContributors, typeof(TEntity));   // the ONE shared read-scope fold
        if (scope is null) return _inner.Search(options, ct);                 // no axis scopes this type ⇒ unchanged
        if (!_canFilter)
            throw new NotSupportedException(
                $"Vector search on a scoped entity '{typeof(TEntity).Name}' fails closed: adapter " +
                $"'{_inner.GetType().Name}' does not announce metadata filtering ({nameof(VectorCaps.Filters)}), so the " +
                $"read-scope predicate cannot be enforced (GAP C 0.3 / DATA-0106 §4).");
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

    // Run the SAME fail-closed guards the data + storage paths use (IStorageGuard / TenantStorageGuard): [HostScoped]
    // is exempt, no-scope under Closed throws, dev-Open warns. Off (no guard) ⇒ no-op ⇒ byte-identical.
    private void RunGuards()
    {
        for (var i = 0; i < _guards.Length; i++) _guards[i].Guard(typeof(TEntity));
    }

    // Stamp EVERY applicable managed field's value into the metadata (the registered ManagedFieldDescriptor seam —
    // equality and non-equality alike, so a read-filter predicate has its fields to match). Off / non-dict metadata ⇒
    // unchanged (a non-dict cannot be stamped and is excluded by the read-filter on read — safe, not a leak).
    private static object? StampScope(object? metadata)
    {
        if (ManagedFieldRegistry.IsEmpty) return metadata;
        var managed = ManagedFieldRegistry.ForType(typeof(TEntity));
        if (managed.Count == 0) return metadata;
        if (!TryGetMutableDict(metadata, out var dict)) return metadata;
        var stamped = false;
        for (var i = 0; i < managed.Count; i++)
        {
            var v = managed[i].ValueProvider();
            if (v is null) continue;                                  // off / host / not-applicable
            dict[managed[i].StorageName] = v;
            stamped = true;
        }
        return stamped ? dict : metadata;
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
