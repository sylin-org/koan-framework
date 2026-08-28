using DuckDB.NET.Data;
using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Analytics;
using Koan.Data.Connector.DuckDb.Infrastructure;
using Koan.Data.Connector.DuckDb.Runtime;
using Koan.Data.Core;
using Koan.Data.Relational;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.DuckDb;

[ProviderPriority(Constants.Priority)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "DuckDB",
    DeploymentKind = DeploymentKind.InProcess,
    Capabilities = ["protocol=file"],
    Volumes = ["./Data/duckdb:/data"],
    AppEnv = ["Koan__Data__DuckDb__ConnectionString=Data Source=/data/app.duckdb"],
    Scheme = "file", Host = "", EndpointPort = 0,
    UriPattern = "Data Source={path}", LocalScheme = "file", LocalHost = "", LocalPort = 0,
    LocalPattern = "Data Source={path}")]
public sealed class DuckDbAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => ["duckdb"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.DuckDb"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider, StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => DuckDbFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords,
        ["sql"],
        enforcesReadLanes: true);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        var connections = services.GetRequiredService<DuckDbConnections>();
        return new RelationalSourceIntegration(
            lane => connections.Create(route.ReadLanes[lane], route.Source, nonCreating: true),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                // Read lanes are enforced by the engine, not by convention: a read-only transaction rejects
                // the first write attempt (DuckDB has no query_only switch).
                var begin = connection.CreateCommand();
                begin.CommandText = "BEGIN TRANSACTION READ ONLY";
                await begin.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return new ReadOnlyDuckDbTransaction((DuckDBConnection)connection);
            },
            new DuckDbInspector(route, connections));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull => new DuckDbRepository<TEntity, TKey>(services, ResolveRoute(services, source), this);

    internal DuckDbRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<DuckDbOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, defaults.ConnectionString, this);
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString) &&
                                  !string.Equals(lane.Value.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static lane => lane.Key, static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new DuckDbRoute(
            resolvedSource,
            connection,
            defaults,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<DuckDbOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default
        };
    }
}
