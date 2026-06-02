using Koan.Core.Capabilities;

namespace Koan.Data.Vector.Abstractions.Capabilities;

/// <summary>
/// The vector pillar's capability tokens (ARCH-0084). Tokens live in the vector abstractions so a
/// vector reference surfaces them (Reference = Intent). The <see cref="Filters"/> token carries a
/// <c>FilterSupport</c> detail (built via <c>FilterSupport.Uniform</c> — vector metadata is schemaless).
/// </summary>
public static class VectorCaps
{
    /// <summary>k-NN search.</summary>
    public static readonly Capability Knn = new("vector.knn");
    /// <summary>Metadata filtering (carries a <c>FilterSupport</c> detail).</summary>
    public static readonly Capability Filters = new("vector.filters");
    /// <summary>Hybrid (vector + keyword/lexical) search.</summary>
    public static readonly Capability Hybrid = new("vector.hybrid");
    /// <summary>Native opaque continuation tokens for pagination.</summary>
    public static readonly Capability NativeContinuation = new("vector.nativeContinuation");
    /// <summary>Streaming result delivery.</summary>
    public static readonly Capability StreamingResults = new("vector.streamingResults");
    /// <summary>Multiple vectors per entity.</summary>
    public static readonly Capability MultiVectorPerEntity = new("vector.multiVectorPerEntity");
    /// <summary>Bulk upsert.</summary>
    public static readonly Capability BulkUpsert = new("vector.bulkUpsert");
    /// <summary>Bulk delete.</summary>
    public static readonly Capability BulkDelete = new("vector.bulkDelete");
    /// <summary>Atomic batches.</summary>
    public static readonly Capability AtomicBatch = new("vector.atomicBatch");
    /// <summary>Score normalization.</summary>
    public static readonly Capability ScoreNormalization = new("vector.scoreNormalization");
    /// <summary>Runtime collection/class creation for partition isolation.</summary>
    public static readonly Capability DynamicCollections = new("vector.dynamicCollections");

    /// <summary>
    /// Resolves the vector capabilities of <paramref name="source"/> from its
    /// <see cref="IDescribesCapabilities"/> declaration; returns an empty set when it declares none.
    /// </summary>
    public static CapabilitySet Describe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CapabilityResolver.TryDescribe(source, owner) ?? new CapabilitySet(owner);
    }
}
