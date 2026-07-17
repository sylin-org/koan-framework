namespace Koan.Data.Abstractions;

/// <summary>
/// Complete data adapter contract: record-repository creation. Discovery, naming and source-routing come from
/// <see cref="IAdapterFactory"/> (the marker base — <see cref="Naming.INamingProvider.Provider"/> +
/// declarative aliases/reference identities); this adds the record <see cref="Create{TEntity,TKey}"/>.
/// </summary>
public interface IDataAdapterFactory : IAdapterFactory
{
    /// <summary>
    /// Create repository for entity with specified source context.
    /// Source determines connection string and adapter-specific settings.
    /// </summary>
    /// <param name="sp">Service provider for dependency resolution</param>
    /// <param name="source">Source name for connection/settings lookup (defaults to "Default")</param>
    /// <returns>Data repository configured for the specified source</returns>
    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
