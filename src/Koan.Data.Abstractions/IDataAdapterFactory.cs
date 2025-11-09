using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Abstractions;

/// <summary>
/// Complete data adapter contract: repository creation and storage naming.
/// Each adapter must implement both concerns.
/// </summary>
public interface IDataAdapterFactory : INamingProvider
{
    // Provider property inherited from INamingProvider
    bool CanHandle(string provider);

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
