using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Aodb;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
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
/// <item><b>Search</b>: ANDs the write-stamped slice of the composed <see cref="ReadScopeFold.Compose"/> Aodb (the ONE
/// provenance-tagging composer the facade + diagnostic share) into the query — so tenancy's equality AND a moderation
/// axis's non-equality predicate both scope a KNN, with no hand-rolled predicate here. An OPERATION-SOURCED axis
/// (e.g. [SoftDelete]'s <c>__deleted</c>, set on the DATA row's delete, never stamped into the independent vector store)
/// is excluded by its <see cref="FieldProvenance"/> via <see cref="Aodb.CombineWriteStamped"/> — store-aware push as
/// data, not a per-plane fork.</item>
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
    private readonly OverlayNamingRule? _overlayNaming;

    public ScopedVectorRepository(IVectorSearchRepository<TEntity, TKey> inner, IServiceProvider sp)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // Resolve the SAME registered contributor seams the data path consumes (boot-stable singletons).
        _readContributors = sp.GetServices<IReadFilterContributor>().ToArray();
        _guards = sp.GetServices<IStorageGuard>().ToArray();
        _canFilter = VectorCaps.Describe(inner, inner.GetType().Name).Has(VectorCaps.Filters);
        // ARCH-0102 §5: the adapter DECLARES how to spell overlay (__-marked) fields in its store; the framework
        // applies it at write-stamp AND read-filter from this one declaration. null ⇒ the __ default (no rename).
        _overlayNaming = (inner as IOverlayNamingAware)?.OverlayNaming;
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
        // ARCH-0102 §3: the vector index write-stamps ambient fields but never runs an operation (e.g. soft-delete),
        // so it pushes only the write-stamped, non-operation-sourced slice of the composed Aodb — the one composer
        // tags provenance, the decorator just realizes its store's slice (the former bespoke FilterMentions fork is gone).
        var scope = ReadScopeFold.Compose(_readContributors, typeof(TEntity)).CombineWriteStamped();
        if (scope is null) return _inner.Search(options, ct);                 // no enforceable axis scopes this type ⇒ unchanged
        if (!_canFilter)
            throw new NotSupportedException(
                $"Vector search on a scoped entity '{typeof(TEntity).Name}' fails closed: adapter " +
                $"'{_inner.GetType().Name}' does not announce metadata filtering ({nameof(VectorCaps.Filters)}), so the " +
                $"read-scope predicate cannot be enforced (GAP C 0.3 / DATA-0106 §4).");
        scope = RenameOverlayFields(scope);                                   // §5: spell __ fields per the adapter's rule
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

    public void Describe(ICapabilities caps)
    {
        VectorCaps.Describe(_inner, _inner.GetType().Name).CopyInto(caps);
        // ARCH-0103 §6 — the vector plane realizes all three AODB isolation modes through THIS decorator (the one
        // chokepoint wrapping every vector adapter), so the isolation tokens are declared here, not on the inner:
        //   • Container + Database — the shared name-mangling floor (VectorAdapterNaming over StorageNameGenerator)
        //     folds the ambient partition and the Database-mode routed source into a DISTINCT physical
        //     collection/index/class for EVERY vector adapter uniformly, so both modes are realized fleet-wide.
        //     (Database here is the LAZY name-fold posture — any ambient source resolves to a distinct collection, so
        //     there is no external-only fail-close-on-unconfigured; that posture clause of DataCaps.Isolation.DatabaseScoped
        //     is record-plane-specific, see the token doc.)
        //   • RowScoped — the overlay write-stamp + read-filter (above) realizes Shared, but only when the inner
        //     adapter announces metadata filtering (VectorCaps.Filters); the Shared path already fail-closes on its
        //     absence, so the token is gated on the same bit. Each token is co-defined with its proof
        //     (ARCH-0094) by VectorAodbConformanceSpecsBase — declare-but-not-realize goes red.
        caps.Add(DataCaps.Isolation.ContainerScoped).Add(DataCaps.Isolation.DatabaseScoped);
        if (_canFilter) caps.Add(DataCaps.Isolation.RowScoped);
    }

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
    private object? StampScope(object? metadata)
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
            dict[Rename(managed[i].StorageName)] = v;                 // §5: same overlay rename as the read filter
            stamped = true;
        }
        return stamped ? dict : metadata;
    }

    // §5 — spell an overlay field name per the adapter's declared rule (null ⇒ unchanged). The SAME rule renames
    // the write-stamp key and the read-filter field, so write-name == read-name by construction (FC-6).
    private string Rename(string name) => _overlayNaming is null ? name : _overlayNaming.Apply(name);

    // Rewrite a read-scope predicate's overlay field names per the rule. Closed Filter hierarchy ⇒ exhaustive;
    // only the framework scope is rewritten (user metadata is never in the __ namespace). null ⇒ identity.
    private Filter RenameOverlayFields(Filter f)
    {
        if (_overlayNaming is null) return f;
        return f switch
        {
            FieldFilter ff => ff with { Field = new FieldPath(ff.Field.Segments.Select(_overlayNaming.Apply).ToArray()) },
            AllOf a => new AllOf(a.Operands.Select(RenameOverlayFields).ToArray()),
            AnyOf a => new AnyOf(a.Operands.Select(RenameOverlayFields).ToArray()),
            Not n => new Not(RenameOverlayFields(n.Operand)),
            _ => f,                                                   // ClrFilter: opaque, never pushed to a vector store
        };
    }

    private static bool TryGetMutableDict(object? metadata, out Dictionary<string, object?> dict)
    {
        // Ordinal (not OrdinalIgnoreCase): the managed StorageName is canonical, and an ignore-case copy would THROW
        // on a user metadata pair that differs only in case (e.g. "Category"/"category"). Note: at runtime object? ==
        // object, so the <string,object?> cases also match a Dictionary<string,object>.
        switch (metadata)
        {
            case null: dict = new Dictionary<string, object?>(StringComparer.Ordinal); return true;
            case IDictionary<string, object?> d: dict = new Dictionary<string, object?>(d, StringComparer.Ordinal); return true;
            case IReadOnlyDictionary<string, object?> d: dict = new Dictionary<string, object?>(d, StringComparer.Ordinal); return true;
            default: dict = null!; return false;
        }
    }
}
