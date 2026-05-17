using System.Linq.Expressions;

namespace Koan.Data.Abstractions;

public interface ILinqQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
}
