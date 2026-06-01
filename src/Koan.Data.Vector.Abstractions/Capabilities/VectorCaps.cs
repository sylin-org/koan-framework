using Koan.Core.Capabilities;

namespace Koan.Data.Vector.Abstractions.Capabilities;

/// <summary>
/// The vector pillar's capability tokens (ARCH-0084) plus the enum↔token bridge for the legacy
/// <see cref="VectorCapabilities"/> flag enum. Tokens live in the vector abstractions so a
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

    /// <summary>Yields the tokens corresponding to a legacy <see cref="VectorCapabilities"/> flag set.</summary>
    public static IEnumerable<Capability> From(VectorCapabilities flags)
    {
        if (flags.HasFlag(VectorCapabilities.Knn)) yield return Knn;
        if (flags.HasFlag(VectorCapabilities.Filters)) yield return Filters;
        if (flags.HasFlag(VectorCapabilities.Hybrid)) yield return Hybrid;
        if (flags.HasFlag(VectorCapabilities.NativeContinuation)) yield return NativeContinuation;
        if (flags.HasFlag(VectorCapabilities.StreamingResults)) yield return StreamingResults;
        if (flags.HasFlag(VectorCapabilities.MultiVectorPerEntity)) yield return MultiVectorPerEntity;
        if (flags.HasFlag(VectorCapabilities.BulkUpsert)) yield return BulkUpsert;
        if (flags.HasFlag(VectorCapabilities.BulkDelete)) yield return BulkDelete;
        if (flags.HasFlag(VectorCapabilities.AtomicBatch)) yield return AtomicBatch;
        if (flags.HasFlag(VectorCapabilities.ScoreNormalization)) yield return ScoreNormalization;
        if (flags.HasFlag(VectorCapabilities.DynamicCollections)) yield return DynamicCollections;
    }

    /// <summary>Projects the vector tokens in <paramref name="caps"/> back to the legacy flag enum.</summary>
    public static VectorCapabilities ToVectorCapabilities(CapabilitySet caps)
    {
        ArgumentNullException.ThrowIfNull(caps);
        var flags = VectorCapabilities.None;
        if (caps.Has(Knn)) flags |= VectorCapabilities.Knn;
        if (caps.Has(Filters)) flags |= VectorCapabilities.Filters;
        if (caps.Has(Hybrid)) flags |= VectorCapabilities.Hybrid;
        if (caps.Has(NativeContinuation)) flags |= VectorCapabilities.NativeContinuation;
        if (caps.Has(StreamingResults)) flags |= VectorCapabilities.StreamingResults;
        if (caps.Has(MultiVectorPerEntity)) flags |= VectorCapabilities.MultiVectorPerEntity;
        if (caps.Has(BulkUpsert)) flags |= VectorCapabilities.BulkUpsert;
        if (caps.Has(BulkDelete)) flags |= VectorCapabilities.BulkDelete;
        if (caps.Has(AtomicBatch)) flags |= VectorCapabilities.AtomicBatch;
        if (caps.Has(ScoreNormalization)) flags |= VectorCapabilities.ScoreNormalization;
        if (caps.Has(DynamicCollections)) flags |= VectorCapabilities.DynamicCollections;
        return flags;
    }
}
