using Koan.Core.Capabilities;

namespace Koan.Data.Abstractions.Capabilities;

/// <summary>
/// The data pillar's capability tokens (ARCH-0084) plus the enum↔token bridge that lets the legacy
/// <see cref="QueryCapabilities"/> / <see cref="WriteCapabilities"/> flag enums coexist with the
/// unified model during migration. Tokens live here — not in a global catalog — so referencing the
/// data abstractions is what surfaces them (Reference = Intent).
/// </summary>
public static class DataCaps
{
    /// <summary>Query negotiation tokens (mirror of <see cref="QueryCapabilities"/>).</summary>
    public static class Query
    {
        /// <summary>Provider accepts server-side string queries.</summary>
        public static readonly Capability String = new("query.string");
        /// <summary>Provider accepts LINQ predicates.</summary>
        public static readonly Capability Linq = new("query.linq");
        /// <summary>Provider exposes a fast (approximate-allowed) count.</summary>
        public static readonly Capability FastCount = new("query.fastCount");
        /// <summary>Provider exposes an optimized exact count.</summary>
        public static readonly Capability OptimizedCount = new("query.optimizedCount");
        /// <summary>Filter pushdown — carries a <see cref="Filtering.FilterSupport"/> detail describing the operator set.</summary>
        public static readonly Capability Filter = new("query.filter");
    }

    /// <summary>Write negotiation tokens (mirror of <see cref="WriteCapabilities"/>).</summary>
    public static class Write
    {
        /// <summary>Provider supports bulk upsert.</summary>
        public static readonly Capability BulkUpsert = new("write.bulkUpsert");
        /// <summary>Provider supports bulk delete.</summary>
        public static readonly Capability BulkDelete = new("write.bulkDelete");
        /// <summary>Provider supports atomic batches.</summary>
        public static readonly Capability AtomicBatch = new("write.atomicBatch");
        /// <summary>Provider supports a fast (unsafe-for-hooks) remove path.</summary>
        public static readonly Capability FastRemove = new("write.fastRemove");
    }

    // --- enum -> token bridge (migration scaffolding; retired in the Facet 1 delete stage) ---

    /// <summary>Yields the tokens corresponding to a legacy <see cref="QueryCapabilities"/> flag set.</summary>
    public static IEnumerable<Capability> From(QueryCapabilities flags)
    {
        if (flags.HasFlag(QueryCapabilities.String)) yield return Query.String;
        if (flags.HasFlag(QueryCapabilities.Linq)) yield return Query.Linq;
        if (flags.HasFlag(QueryCapabilities.FastCount)) yield return Query.FastCount;
        if (flags.HasFlag(QueryCapabilities.OptimizedCount)) yield return Query.OptimizedCount;
    }

    /// <summary>Yields the tokens corresponding to a legacy <see cref="WriteCapabilities"/> flag set.</summary>
    public static IEnumerable<Capability> From(WriteCapabilities flags)
    {
        if (flags.HasFlag(WriteCapabilities.BulkUpsert)) yield return Write.BulkUpsert;
        if (flags.HasFlag(WriteCapabilities.BulkDelete)) yield return Write.BulkDelete;
        if (flags.HasFlag(WriteCapabilities.AtomicBatch)) yield return Write.AtomicBatch;
        if (flags.HasFlag(WriteCapabilities.FastRemove)) yield return Write.FastRemove;
    }

    // --- token -> enum bridge (lets legacy consumers read a CapabilitySet during migration) ---

    /// <summary>Projects the query tokens in <paramref name="caps"/> back to the legacy flag enum.</summary>
    public static QueryCapabilities ToQueryCapabilities(CapabilitySet caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        var flags = QueryCapabilities.None;
        if (caps.Has(Query.String)) flags |= QueryCapabilities.String;
        if (caps.Has(Query.Linq)) flags |= QueryCapabilities.Linq;
        if (caps.Has(Query.FastCount)) flags |= QueryCapabilities.FastCount;
        if (caps.Has(Query.OptimizedCount)) flags |= QueryCapabilities.OptimizedCount;
        return flags;
    }

    /// <summary>Projects the write tokens in <paramref name="caps"/> back to the legacy flag enum.</summary>
    public static WriteCapabilities ToWriteCapabilities(CapabilitySet caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        var flags = WriteCapabilities.None;
        if (caps.Has(Write.BulkUpsert)) flags |= WriteCapabilities.BulkUpsert;
        if (caps.Has(Write.BulkDelete)) flags |= WriteCapabilities.BulkDelete;
        if (caps.Has(Write.AtomicBatch)) flags |= WriteCapabilities.AtomicBatch;
        if (caps.Has(Write.FastRemove)) flags |= WriteCapabilities.FastRemove;
        return flags;
    }

    // --- resolver: native IDescribesCapabilities, else the legacy enum bridge ---

    /// <summary>
    /// Resolves the query + write capabilities of <paramref name="source"/>: its native
    /// <see cref="IDescribesCapabilities"/> declaration when present, otherwise bridged from the
    /// legacy <see cref="IQueryCapabilities"/> / <see cref="IWriteCapabilities"/> markers.
    /// </summary>
    public static CapabilitySet Describe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (CapabilityResolver.TryDescribe(source, owner) is { } native) return native;

        var set = new CapabilitySet(owner);
        if (source is IQueryCapabilities q) foreach (var t in From(q.Capabilities)) set.Add(t);
        if (source is IWriteCapabilities w) foreach (var t in From(w.Writes)) set.Add(t);
        return set;
    }
}
