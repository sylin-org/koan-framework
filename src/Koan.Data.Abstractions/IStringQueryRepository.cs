using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface IStringQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, DataQueryOptions? options, CancellationToken ct = default);
}
