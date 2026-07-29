using Koan.Data.Abstractions;

namespace Koan.Data.Core.Execution;

/// <summary>Internal Data-owned outcome surface implemented by the application-facing repository facade.</summary>
internal interface IDataMutationOutcomes<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    Task<MutationResult<TEntity, TKey>> UpsertWithOutcome(TEntity model, CancellationToken ct);
    Task<MutationResult<TEntity, TKey>> DeleteWithOutcome(TKey id, CancellationToken ct);
}
