using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
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
/// <item><b>Search</b>: ANDs the <see cref="ReadScopeFold"/> of the registered <see cref="IReadFilterContributor"/>s
/// (the ONE read-scope fold the facade read path uses) into the query — so tenancy's equality AND a moderation axis's
/// non-equality predicate both scope a KNN, with no hand-rolled predicate here. EXCLUDES an operation-override axis
/// (e.g. [SoftDelete]'s <c>__deleted</c>, registered in <c>OperationOverrideRegistry</c>): its field is set on the DATA
/// row's delete, never stamped into the independent vector store, so it is unenforceable here — see <c>FoldReadScope</c>.</item>
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
        var scope = FoldReadScope(typeof(TEntity));
        if (scope is null) return _inner.Search(options, ct);                 // no axis scopes this type ⇒ unchanged
        if (!_canFilter)
            throw new NotSupportedException(
                $"Vector search on a scoped entity '{typeof(TEntity).Name}' fails closed: adapter " +
                $"'{_inner.GetType().Name}' does not announce metadata filtering ({nameof(VectorCaps.Filters)}), so the " +
                $"read-scope predicate cannot be enforced (GAP C 0.3 / DATA-0106 §4).");
        scope = RenameOverlayFields(scope);                                   // §5: spell __ fields per the adapter's rule
        var combined = options.Filter is null ? scope : Filter.All(options.Filter, scope);
        return _inner.Search(options with { Filter = combined }, ct);
    }

    // The vector read-scope: the shared ReadScopeFold, MINUS any axis whose field the vector store cannot enforce.
    // An OPERATION-OVERRIDE axis (e.g. [SoftDelete]'s __deleted) is written into the DATA row's lifecycle on delete,
    // never stamped into the independent vector metadata on write — so its predicate references a field absent from
    // every vector record. Folding it is unenforceable (the deleted row's vector still exists ⇒ it was never isolated)
    // and would OVER-FILTER (an equality over the missing field matches nothing); excluding it keeps the STAMPED axes
    // (tenant, moderation) enforced. (Vector lifecycle-sync of an operation-override is a separate follow-on; until
    // then a soft-deleted vector remains searchable — a visibility gap surfaced honestly here, never a silent break.)
    private Filter? FoldReadScope(Type entityType)
    {
        if (OperationOverrideRegistry.IsEmpty) return ReadScopeFold.Fold(_readContributors, entityType);   // byte-identical
        var overrideField = OperationOverrideRegistry.ForDelete(entityType)?.Field;
        if (overrideField is null) return ReadScopeFold.Fold(_readContributors, entityType);   // no override axis here
        List<Filter>? survivors = null;
        for (var i = 0; i < _readContributors.Length; i++)
        {
            var f = _readContributors[i].ReadFilter(entityType);
            if (f is null) continue;
            if (FilterMentions(f, overrideField)) continue;       // an operation-override axis the vector store can't sync
            (survivors ??= new()).Add(f);
        }
        if (survivors is null) return null;
        return survivors.Count == 1 ? survivors[0] : Filter.All(survivors.ToArray());
    }

    // Does the predicate tree reference the (single-segment, managed) field name? Closed Filter hierarchy ⇒ exhaustive.
    private static bool FilterMentions(Filter f, string field) => f switch
    {
        FieldFilter ff => string.Equals(ff.Field.Leaf, field, StringComparison.Ordinal),
        AllOf a => AnyMentions(a.Operands, field),
        AnyOf a => AnyMentions(a.Operands, field),
        Not n => FilterMentions(n.Operand, field),
        _ => false,                                               // ClrFilter: opaque residual, never pushed to a vector store
    };

    private static bool AnyMentions(IReadOnlyList<Filter> ops, string field)
    {
        for (var i = 0; i < ops.Count; i++) if (FilterMentions(ops[i], field)) return true;
        return false;
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
