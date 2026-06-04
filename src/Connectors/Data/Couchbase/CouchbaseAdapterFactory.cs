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

    // Couchbase isolates a partition through a native scope (bucket.scope.collection), not a name suffix —
    // CouchbaseClusterProvider.GetCollectionContext routes EntityContext.Current.Partition into the scope
    // position via FormatScope. So the capability announces EncodePartitionInName = false: the framework
    // generates just the collection name, and a fixed Collection / CollectionName callback overrides it.
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            // Couchbase collection names allow only [A-Za-z0-9_-%] — a '.' separator (the FullNamespace
            // default) produces an invalid collection name and CreateCollectionAsync fails. Use '_'.
            Separator = "_",
            Casing = NameCasing.AsIs,
            EncodePartitionInName = false,
            NameOverride = entityType => !string.IsNullOrWhiteSpace(options.Collection)
                ? options.Collection!.Trim()
                : options.CollectionName?.Invoke(entityType),
        };
    }

    /// <summary>
    /// Format a partition value as a Couchbase scope identifier (alphanumeric / underscore / hyphen /
    /// percent, max 30 bytes). Used by CouchbaseClusterProvider when mapping EntityContext.Current.Partition
    /// onto bucket.scope.collection.
    /// </summary>
    public static string FormatScope(string partition)
    {
        if (string.IsNullOrEmpty(partition)) return partition;
        var sb = new System.Text.StringBuilder(partition.Length);
        var faithful = true;
        foreach (var c in partition)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '%') sb.Append(c);
            else { sb.Append('_'); faithful = false; }
        }
        var sanitized = sb.ToString();

        const int maxScopeBytes = 30; // Couchbase scope/collection identifier limit
        // Injective: return the sanitized form only when it FAITHFULLY represents the original — no character
        // was replaced AND it fits the limit. Otherwise append a deterministic hash of the ORIGINAL so distinct
        // partitions can never collapse onto one scope. Both lossy '_' replacement (e.g. "a.b" and "a_b" both
        // sanitize to "a_b" — '.' passes the front-door validator but Couchbase scopes forbid it) and
        // over-length truncation are collision sources; hashing the original closes both. (The previous code
        // guarded only length, and hashed the sanitized form — which still collided distinct originals that
        // sanitized alike.)
        if (faithful && NamingUtils.ByteLength(sanitized) <= maxScopeBytes) return sanitized;
        var hash = NamingUtils.ShortHash(partition, 8);
        return NamingUtils.TrimToBytes(sanitized, maxScopeBytes - hash.Length - 1) + "_" + hash;
    }
}

