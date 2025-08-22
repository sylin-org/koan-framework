namespace Sora.Data.Vector.Abstractions;

[Flags]
public enum VectorCapabilities
{
    None = 0,
    Knn = 1 << 0,
    Filters = 1 << 1,
    Hybrid = 1 << 2,
    PaginationToken = 1 << 3,
    StreamingResults = 1 << 4,
    MultiVectorPerEntity = 1 << 5,
    BulkUpsert = 1 << 6,
    BulkDelete = 1 << 7,
    AtomicBatch = 1 << 8,
    ScoreNormalization = 1 << 9,
}