using System.Linq.Expressions;

namespace Koan.Data.Abstractions;

public interface ILinqQueryRepositoryWithOptions<TEntity, TKey> : ILinqQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default);
}