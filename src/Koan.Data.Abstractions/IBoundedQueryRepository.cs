namespace Koan.Data.Abstractions;

/// <summary>
/// Optional scan-provider seam that refuses to materialize more than a caller-approved candidate
/// count. It is deliberately separate from <see cref="IQueryRepository{TEntity,TKey}"/>: ordinary
/// query correctness does not imply a bounded scan.
/// </summary>
public interface IBoundedQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<BoundedQueryResult<TEntity>> QueryBoundedCandidates(
        QueryDefinition query,
        int maxCandidates,
        CancellationToken ct = default);
}
