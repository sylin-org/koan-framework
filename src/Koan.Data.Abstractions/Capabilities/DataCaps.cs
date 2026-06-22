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
        /// <summary>Provider supports an atomic conditional replace (compare-and-set by Id) — see
        /// <see cref="IConditionalWriteRepository{TEntity,TKey}"/>. Enables contention-free claiming (JOBS-0005 §20.3).</summary>
        public static readonly Capability ConditionalReplace = new("write.conditionalReplace");
    }

    /// <summary>Row-isolation negotiation tokens (DATA-0105 / ARCH-0095). Axis-free: the token names the
    /// adapter <b>guarantee</b>, not the consumer that needs it (Koan.Tenancy <c>Require</c>s it).</summary>
    public static class Isolation
    {
        /// <summary>Provider can persist and filter a framework-managed row discriminator (a managed field, see
        /// <see cref="Pipeline.ManagedFieldDescriptor"/>): it stores the injected key with each record AND pushes
        /// a scalar equality on it. A managed-scoped entity routed to an adapter lacking this token fails closed.</summary>
        public static readonly Capability RowScoped = new("isolation.rowScoped");
    }

    /// <summary>Retention negotiation tokens.</summary>
    public static class Retention
    {
        /// <summary>Provider supports a store-native TTL index — a <c>[Index(Ttl = true)]</c> timestamp property whose
        /// rows the store expires automatically once it is in the past (e.g. Mongo <c>expireAfterSeconds = 0</c>).
        /// Adapters without this token ignore TTL indexes entirely. (JOBS-0005 §20.4)</summary>
        public static readonly Capability TtlIndex = new("retention.ttlIndex");
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
