using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public interface IVectorAdapterFactory
{
    bool CanHandle(string provider);
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}