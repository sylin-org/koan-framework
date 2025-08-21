using Sora.Data.Abstractions;

namespace Sora.Data.Vector.Abstractions;

// Re-export or shim minimal contracts for providers to depend on. For now,
// we type-forward to Sora.Data.Abstractions to avoid churn; future move can relocate types.

public interface IVectorAdapterFactory
{
    bool CanHandle(string provider);
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}

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

public sealed record VectorQueryOptions(
    float[] Query,
    int? TopK = null,
    string? ContinuationToken = null,
    object? Filter = null,
    TimeSpan? Timeout = null,
    string? VectorName = null
);

public sealed record VectorMatch<TKey>(TKey Id, double Score, object? Metadata = null) where TKey : notnull;

public enum VectorTotalKind
{
    Unknown = 0,
    Exact = 1,
    Estimated = 2,
}

public sealed record VectorQueryResult<TKey>(
    IReadOnlyList<VectorMatch<TKey>> Matches,
    string? ContinuationToken,
    VectorTotalKind TotalKind = VectorTotalKind.Unknown
) where TKey : notnull;

public interface IVectorSearchRepository<TEntity, TKey> where TEntity : IEntity<TKey> where TKey : notnull
{
    Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default);
    Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default);

    Task VectorEnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask; // optional convenience
    Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default);
}

public interface IVectorCapabilities
{
    VectorCapabilities Capabilities { get; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class VectorAdapterAttribute : Attribute
{
    public string Provider { get; }
    public VectorAdapterAttribute(string provider) => Provider = provider;
}

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class VectorEmbeddingAttribute : Attribute
{
    public string? Name { get; }
    public VectorEmbeddingAttribute(string? name = null) => Name = name;
}
