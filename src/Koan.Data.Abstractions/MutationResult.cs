using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Typed Entity mutation result with a safe commit fact.</summary>
public sealed record MutationResult<TEntity, TKey>(
    TKey Key,
    MutationOutcome Outcome,
    TEntity? Entity,
    DataCommitOutcome CommitOutcome)
    where TKey : notnull;
