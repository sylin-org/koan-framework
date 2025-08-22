using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Vector;

public interface IVectorService
{
    IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
}