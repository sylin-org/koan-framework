using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Redis.Infrastructure;
using Koan.Data.Connector.Redis.Runtime;
using Koan.Data.Core;
using Koan.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis;

[ProviderPriority(Constants.Priority)]
public sealed class RedisAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [Constants.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Redis"];

    public void DescribeClaims(IDataClaims claims) => Runtime.RedisFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.None,
        ["function"],
        enforcesReadLanes: true);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        var mapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        return new RedisRepository<TEntity, TKey>(
            services,
            this,
            route,
            mapping);
    }

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source) =>
        new RedisSourceIntegration(ResolveRoute(services, source));

    internal RedisRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<RedisOptions>>().Value;
        Validate(defaults);
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connections = services.GetRequiredService<IRedisConnectionProvider>();
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, connections.DefaultConnectionString, this);
        var database = Setting(configuration, registry, resolvedSource, "Database", defaults.Database);
        var maxQuery = Setting(configuration, registry, resolvedSource, "MaxQueryEntries", defaults.MaxQueryEntries);
        var maxBulk = Setting(configuration, registry, resolvedSource, "MaxBulkEntries", defaults.MaxBulkEntries);
        if (database < 0) throw new InvalidOperationException("Redis Database must be zero or greater.");
        if (maxQuery <= 0 || maxBulk <= 0)
            throw new InvalidOperationException("Redis MaxQueryEntries and MaxBulkEntries must be positive.");
        var definition = registry.GetSource(resolvedSource);
        var lanes = definition?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(static lane => lane.Key, static lane => lane.Value.ConnectionString, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new RedisRoute(
            resolvedSource,
            connectionString,
            connections.GetConnection(connectionString),
            database,
            maxQuery,
            maxBulk,
            definition?.StorageLifecycle ?? StorageLifecycle.Managed,
            definition?.Access ?? DataSourceAccess.ReadWrite,
            registry.GetPlan(resolvedSource, Provider, connectionString),
            lanes,
            connections);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<RedisOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
            EncodePartitionInName = true
        };
    }

    private T Setting<T>(IConfiguration configuration, DataSourceRegistry registry, string source, string key, T fallback) =>
        AdapterConnectionResolver.GetSourceSetting(configuration, registry, Provider, source, key, fallback, this);

    private static void Validate(RedisOptions options)
    {
        if (options.MaxQueryEntries <= 0 || options.MaxBulkEntries <= 0)
            throw new InvalidOperationException("Redis MaxQueryEntries and MaxBulkEntries must be positive.");
    }
}

internal sealed record RedisRoute(
    string Source,
    string ConnectionString,
    IConnectionMultiplexer Connection,
    int Database,
    int MaxQueryEntries,
    int MaxBulkEntries,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes,
    IRedisConnectionProvider Connections)
{
    internal IDatabase Data => Connection.GetDatabase(Database);
}
