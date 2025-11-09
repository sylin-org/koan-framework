using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.Couchbase;

[ProviderPriority(30)]
[KoanService(ServiceKind.Database, shortCode: "couchbase", name: "Couchbase",
    ContainerImage = "couchbase/server",
    DefaultTag = "latest",
    DefaultPorts = new[] { 8091, 8092, 8093, 8094, 11210 },
    Capabilities = new[] { "protocol=couchbase" },
    Volumes = new[] { "./Data/couchbase:/opt/couchbase/var" },
    AppEnv = new[] { "Koan__Data__Couchbase__ConnectionString=couchbase://{host}", "Koan__Data__Couchbase__Bucket=Koan" },
    Scheme = "couchbase", Host = "couchbase", EndpointPort = 8091,
    UriPattern = "couchbase://{host}", LocalScheme = "couchbase", LocalHost = "localhost", LocalPort = 8091, LocalPattern = "couchbase://{host}")]
public sealed class CouchbaseAdapterFactory : IDataAdapterFactory
{
    public string Provider => "couchbase";

    public bool CanHandle(string provider)
        => string.Equals(provider, "couchbase", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var provider = sp.GetRequiredService<CouchbaseClusterProvider>();
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        var options = sp.GetRequiredService<IOptions<CouchbaseOptions>>();
        // Note: Couchbase cluster provider is typically shared; source-specific
        // connections would require provider factory-level changes
        return new CouchbaseRepository<TEntity, TKey>(provider, resolver, sp, options);
    }

    // INamingProvider implementation
    public string RepositorySeparator => "#";

    public string GetStorageName(Type entityType, IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;

        // Check adapter-level override FIRST
        if (!string.IsNullOrWhiteSpace(options.Collection))
            return options.Collection;

        if (options.CollectionName != null)
        {
            var overrideName = options.CollectionName(entityType);
            if (!string.IsNullOrWhiteSpace(overrideName))
                return overrideName;
        }

        // Fall back to convention
        var convention = new StorageNameResolver.Convention(
            options.NamingStyle,
            options.Separator ?? ".",
            NameCasing.AsIs);

        return StorageNameResolver.Resolve(entityType, convention);
    }

    public string GetConcretePartition(string partition)
    {
        // Couchbase: Pass-through (accepts most UTF-8 strings)
        return partition;
    }
}

