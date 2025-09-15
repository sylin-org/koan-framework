namespace Koan.Data.Vector.Abstractions;

public sealed record VectorQueryResult<TKey>(
    IReadOnlyList<VectorMatch<TKey>> Matches,
    string? ContinuationToken,
    VectorTotalKind TotalKind = VectorTotalKind.Unknown
) where TKey : notnull;