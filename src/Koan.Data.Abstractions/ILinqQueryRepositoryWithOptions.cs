using System.Linq.Expressions;

namespace Koan.Data.Abstractions;

public interface ILinqQueryRepositoryWithOptions<TEntity, TKey> : ILinqQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// LINQ-predicate query with sort/page/filter hints. Returns a <see cref="RepositoryQueryResult{TEntity}"/>
    /// declaring which capabilities the adapter pushed down.
    /// </summary>
    Task<RepositoryQueryResult<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default);
}
