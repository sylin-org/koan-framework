namespace Koan.Data.Vector.Abstractions;

[Flags]
public enum VectorCapabilities
{
    None = 0,
    Knn = 1 << 0,
    Filters = 1 << 1,
    Hybrid = 1 << 2,
    /// <summary>
    /// Provider supports native continuation/pagination via opaque continuation tokens.
    /// When set, provider can efficiently resume searches without re-executing full queries.
    /// Examples: Weaviate cursor-based pagination, Qdrant offset-based pagination.
    /// </summary>
    NativeContinuation = 1 << 3,
    StreamingResults = 1 << 4,
    MultiVectorPerEntity = 1 << 5,
    BulkUpsert = 1 << 6,
    BulkDelete = 1 << 7,
    AtomicBatch = 1 << 8,
    ScoreNormalization = 1 << 9,
    /// <summary>
    /// Provider can dynamically create collections/classes at runtime for partition isolation.
    /// Required for partition-aware vector operations via <see cref="Koan.Data.Abstractions.Partition.IPartitionContextProvider"/>.
    /// </summary>
    DynamicCollections = 1 << 10,
}