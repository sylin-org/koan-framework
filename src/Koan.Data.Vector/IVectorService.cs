using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

public interface IVectorService
{
    IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
}