namespace Koan.Data.Abstractions;

/// <summary>Result of a provider-enforced bounded candidate read.</summary>
public sealed record BoundedQueryResult<TEntity>(
    IReadOnlyList<TEntity> Items,
    int CandidatesExamined,
    bool CandidateLimitExceeded);
