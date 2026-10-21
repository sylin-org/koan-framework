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

    /// <summary>
    /// One-way identity of the physical store that <paramref name="source"/> opens for this provider: a database
    /// file, a server database and schema, a directory. Two sources that reach one store return the same value,
    /// whatever their names or connection-string spellings; the value never carries raw connection material.
    /// Together with <see cref="Naming.INamingProvider.ResolveStorage"/> it says where a record operation lands, so
    /// an operation over two endpoints (a transfer) can tell when they are one place. Resolving it performs no I/O.
    /// <see langword="null"/> means the adapter cannot tell; the framework then falls back to the source plan's
    /// connection identity, which compares declarations rather than resolved connections.
    /// </summary>
    string? ResolveStoreIdentity(IServiceProvider services, string source) => null;
}
