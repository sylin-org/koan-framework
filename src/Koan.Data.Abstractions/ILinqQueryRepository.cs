using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface ILinqQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default);
}
