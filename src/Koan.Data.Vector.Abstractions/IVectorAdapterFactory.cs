using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Complete vector adapter contract: repository creation and storage naming.
/// Each vector adapter must implement both concerns.
/// </summary>
public interface IVectorAdapterFactory : INamingProvider
{
    // Provider property inherited from INamingProvider
    bool CanHandle(string provider);
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}