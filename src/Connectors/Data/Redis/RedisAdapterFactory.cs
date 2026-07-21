using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Redis;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis;

[ProviderPriority(5)]
public sealed class RedisAdapterFactory : IDataAdapterFactory
{
    public string Provider => "redis";

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(sp, source);
        return new RedisRepository<TEntity, TKey>(
            route.Connection,
            route.Database,
            sp.GetRequiredService<Koan.Data.Core.Semantics.DataSegmentationPlan>());
    }

    internal RedisSourceRoute ResolveRoute(IServiceProvider sp, string source)
    {
        var baseOpts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var connections = sp.GetRequiredService<IRedisConnectionProvider>();

        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config,
            sourceRegistry,
            Provider,
            source,
            connections.DefaultConnectionString,
            this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, Provider, source, "Database", baseOpts.Database, this);

        var connection = connections.GetConnection(connectionString);

        return new RedisSourceRoute(connection, database, source);
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

internal sealed record RedisSourceRoute(
    IConnectionMultiplexer Connection,
    int Database,
    string Source);

