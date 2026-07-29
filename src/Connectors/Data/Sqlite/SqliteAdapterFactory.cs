using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite;

[ProviderPriority(10)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "SQLite",
    DeploymentKind = DeploymentKind.InProcess,
    Capabilities = ["protocol=file"],
    Volumes = ["./Data/sqlite:/data"],
    AppEnv = ["Koan__Data__Sqlite__ConnectionString=Data Source=/data/app.db"],
    Scheme = "file", Host = "", EndpointPort = 0,
    UriPattern = "Data Source={path}", LocalScheme = "file", LocalHost = "", LocalPort = 0,
    LocalPattern = "Data Source={path}")]
public sealed class SqliteAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => ["sqlite3"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Sqlite"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "sqlite3", StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => SqliteFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords,
        ["sql"],
        enforcesReadLanes: true);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        var connections = services.GetRequiredService<SqliteConnections>();
        return new RelationalSourceIntegration(
            lane => connections.Create(route.ReadLanes[lane], route.Source, nonCreating: true),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA query_only = ON";
                await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return ((SqliteConnection)connection).BeginTransaction(deferred: true);
            },
            new SqliteInspector(route, connections));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull => new SqliteRepository<TEntity, TKey>(services, ResolveRoute(services, source));

    internal SqliteRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, defaults.ConnectionString, this);
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString) &&
                                  !string.Equals(lane.Value.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static lane => lane.Key, static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new SqliteRoute(
            resolvedSource,
            connection,
            defaults,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
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
