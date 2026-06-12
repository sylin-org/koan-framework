namespace Koan.Data.AI;

/// <summary>
/// Thrown by <see cref="VectorModelGuard"/> when an incremental embedding write would introduce a
/// second model into a single-model vector index — i.e. create a <b>mixed-space</b> index whose
/// vectors are no longer comparable (AI-0036 P2 / W4). This is the one new hard error the W4 guard
/// adds, fired at the genuine boundary (the write that would corrupt the index), preventing the
/// silent-wrong-neighbours hazard rather than detecting it after the fact.
/// </summary>
/// <remarks>
/// The fix for the caller is to re-index the collection to the new model via the
/// <c>EmbeddingMigrator</c> (which resets the registry as a by-design model transition), not to write
/// the new-model vector into the existing index.
/// </remarks>
public sealed class VectorModelMismatchException : Exception
{
    /// <summary>The entity whose vector index this concerns.</summary>
    public string Entity { get; }

    /// <summary>The partition ("" = default).</summary>
    public string Partition { get; }

    /// <summary>The model that already produced the index's vectors.</summary>
    public string IndexModel { get; }

    /// <summary>The model the incoming write would have used.</summary>
    public string WriteModel { get; }

    public VectorModelMismatchException(string entity, string partition, string indexModel, string writeModel)
        : base($"Vector index for '{entity}'{(string.IsNullOrEmpty(partition) ? "" : $" (partition '{partition}')")} " +
               $"was built with embedding model '{indexModel}', but this write uses '{writeModel}'. Vectors from " +
               $"different models are not comparable — writing it would create a silently-wrong mixed-space index. " +
               $"Re-index to the new model via EmbeddingMigrator instead of writing into the existing index.")
    {
        Entity = entity;
        Partition = partition;
        IndexModel = indexModel;
        WriteModel = writeModel;
    }
}
