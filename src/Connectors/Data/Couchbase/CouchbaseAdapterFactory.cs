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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(System.Type, string?), string> _nameCache = new();

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

    // Partition is a native Couchbase scope (bucket.scope.collection), not a name suffix —
    // CouchbaseClusterProvider.GetCollectionContext routes EntityContext.Current.Partition into
    // the scope position. ResolveStorage therefore returns just the collection name.
    public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
    {
        var trimmed = partition?.Trim();
        var cacheKey = (entityType, string.IsNullOrEmpty(trimmed) ? null : trimmed);
        return _nameCache.GetOrAdd(cacheKey, _ =>
        {
            var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(options.Collection))
                return options.Collection.Trim();

            if (options.CollectionName != null
                && options.CollectionName(entityType) is { } overrideName
                && !string.IsNullOrWhiteSpace(overrideName))
            {
                return overrideName.Trim();
            }

            var convention = new StorageNameResolver.Convention(
                options.NamingStyle,
                options.Separator ?? ".",
                NameCasing.AsIs);
            return StorageNameResolver.Resolve(entityType, convention).Trim();
        });
    }

    /// <summary>
    /// Format a partition value as a Couchbase scope identifier (alphanumeric / underscore /
    /// hyphen / percent, max 30 chars). Used by CouchbaseClusterProvider when mapping
    /// EntityContext.Current.Partition onto bucket.scope.collection.
    /// </summary>
    public static string FormatScope(string partition)
    {
        if (string.IsNullOrEmpty(partition)) return partition;
        var span = partition.AsSpan();
        var sb = new System.Text.StringBuilder(span.Length);
        for (int i = 0; i < span.Length; i++)
        {
            var c = span[i];
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '%' ? c : '_');
        }
        var sanitized = sb.ToString();
        return sanitized.Length <= 30 ? sanitized : sanitized[..30];
    }
}

