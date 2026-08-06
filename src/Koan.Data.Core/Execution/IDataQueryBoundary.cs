using Koan.Data.Abstractions;

namespace Koan.Data.Core.Execution;

/// <summary>
/// Internal seam between provider candidate execution and application-visible Entity materialization.
/// It lets Data finish residual filtering, sorting, paging, and projection before Lifecycle observes a row.
/// </summary>
internal interface IDataQueryBoundary<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    Task<RepositoryQueryResult<TEntity>> QueryCandidates(
        QueryDefinition query,
        CancellationToken ct = default);

    Task<BoundedQueryResult<TEntity>> QueryBoundedCandidatesRaw(
        QueryDefinition query,
        int maxCandidates,
        CancellationToken ct = default);

    Task MaterializeVisible(
        IReadOnlyList<TEntity> entities,
        CancellationToken ct = default);

    ValueTask MaterializeVisible(
        TEntity entity,
        CancellationToken ct = default);
}
