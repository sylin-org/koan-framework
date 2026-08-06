namespace Koan.Data.Vector.Abstractions;

/// <summary>A bounded vector result with no fabricated global total.</summary>
public sealed record VectorSearchResult<TKey>(
    IReadOnlyList<VectorMatch<TKey>> Items,
    string? Continuation,
    VectorSearchExecution Execution)
    where TKey : notnull;
