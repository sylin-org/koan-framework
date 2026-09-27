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
public sealed class DuckDbAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory, IDataAdapterSetup
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => ["duckdb"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.DuckDb"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider, StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => DuckDbFeatures.Declare(claims);

    public DataAdapterSetupDescriptor DescribeSetup() => new("DuckDB",
    [
        new(nameof(DuckDbOptions.ConnectionString), "Connection string",
            DataProviderSetupFieldKind.ConnectionString, Placeholder: "Data Source=data/app.duckdb")
    ]);

    public async Task<DataProviderProbeResult> Probe(
        IServiceProvider services,
        DataProviderProbeContext candidate,
        CancellationToken ct = default)
    {
        var connectionString = candidate.Require(nameof(DuckDbOptions.ConnectionString));
        if (connectionString.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DuckDB candidate setup requires a concrete connection string.");
        var connections = services.GetRequiredService<DuckDbConnections>();
        var (path, memory) = connections.DescribeSource(connectionString);
        if (!memory && !string.IsNullOrWhiteSpace(path) && !path.Contains("://", StringComparison.Ordinal))
        {
            var anchored = connections.AnchorDataSource(path);
            if (!File.Exists(anchored))
            {
                var parent = Path.GetDirectoryName(anchored);
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                    throw new DirectoryNotFoundException();
                return DataProviderProbeResult.Reachable(
                    "The DuckDB database can be provisioned at the submitted file path.");
            }
        }

        await using var connection = memory
            ? new DuckDBConnection("Data Source=:memory:")
            : connections.Create(connectionString, "CandidateSetup", nonCreating: true);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return DataProviderProbeResult.Ready("The DuckDB target answered a read-only query.");
    }

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
