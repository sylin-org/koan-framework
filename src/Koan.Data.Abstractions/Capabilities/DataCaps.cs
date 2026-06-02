using Koan.Core.Capabilities;

namespace Koan.Data.Abstractions.Capabilities;

/// <summary>
/// The data pillar's capability tokens (ARCH-0084). Tokens live here — not in a global catalog — so
/// referencing the data abstractions is what surfaces them (Reference = Intent).
/// </summary>
public static class DataCaps
{
    /// <summary>Query negotiation tokens.</summary>
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

    /// <summary>Write negotiation tokens.</summary>
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

    /// <summary>
    /// Resolves the data capabilities of <paramref name="source"/> from its
    /// <see cref="IDescribesCapabilities"/> declaration; returns an empty set when it declares none.
    /// </summary>
    public static CapabilitySet Describe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CapabilityResolver.TryDescribe(source, owner) ?? new CapabilitySet(owner);
    }
}
