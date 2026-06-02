using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis;

[ProviderPriority(5)]
[KoanService(ServiceKind.Cache, shortCode: "redis", name: "Redis",
    ContainerImage = "redis",
    DefaultTag = "7",
    DefaultPorts = new[] { 6379 },
    Capabilities = new[] { "protocol=redis" },
    Volumes = new[] { "./Data/redis:/data" },
    AppEnv = new[] { "Koan__Data__Redis__Endpoint={scheme}://{host}:{port}" },
    Scheme = "redis", Host = "redis", EndpointPort = 6379, UriPattern = "redis://{host}:{port}",
    LocalScheme = "redis", LocalHost = "localhost", LocalPort = 6379, LocalPattern = "redis://{host}:{port}")]
public sealed class RedisAdapterFactory : IDataAdapterFactory
{
    public string Provider => "redis";

    public bool CanHandle(string provider) => string.Equals(provider, "redis", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<RedisOptions>>();
        var muxer = sp.GetRequiredService<IConnectionMultiplexer>();
        // Note: Redis connection multiplexer is typically shared; source-specific
        // connections would require factory-level changes to support multiple IConnectionMultiplexer instances
        return new RedisRepository<TEntity, TKey>(opts, muxer, sp.GetService<ILoggerFactory>());
    }

    // The partition separator must NOT be ':' — Redis key delimiter is ':', and the keyspace scan pattern
    // is "{keyspace}:*". A ':' separator made the default keyspace pattern match partition-suffixed keys
    // (e.g. "widgets_surface:*" would match "widgets_surface:alpha:p-001"), leaking partition data into the
    // default set. '#' keeps them disjoint.
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };
}

