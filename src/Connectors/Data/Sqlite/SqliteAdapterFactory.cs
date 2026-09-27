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
using Microsoft.Extensions.Hosting;
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
public sealed class SqliteAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory, IDataAdapterSetup
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => ["sqlite3"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Sqlite"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "sqlite3", StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => SqliteFeatures.Declare(claims);

    public DataAdapterSetupDescriptor DescribeSetup() => new("SQLite",
    [
        new(nameof(SqliteOptions.ConnectionString), "Connection string",
            DataProviderSetupFieldKind.ConnectionString, Placeholder: "Data Source=data/app.db")
    ]);

    public async Task<DataProviderProbeResult> Probe(
        IServiceProvider services,
        DataProviderProbeContext candidate,
        CancellationToken ct = default)
    {
        var connectionString = candidate.Require(nameof(SqliteOptions.ConnectionString));
        if (connectionString.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("SQLite candidate setup requires a concrete connection string.");
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var memory = builder.Mode == SqliteOpenMode.Memory ||
                     builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
        if (!memory && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var root = services.GetService<IHostEnvironment>()?.ContentRootPath;
            var path = Path.IsPathRooted(builder.DataSource) || string.IsNullOrWhiteSpace(root)
                ? Path.GetFullPath(builder.DataSource)
                : Path.GetFullPath(Path.Combine(root, builder.DataSource));
            if (!File.Exists(path))
            {
                var parent = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                    throw new DirectoryNotFoundException();
                return DataProviderProbeResult.Reachable(
                    "The SQLite database can be provisioned at the submitted file path.");
            }
            builder.DataSource = path;
            builder.Mode = SqliteOpenMode.ReadOnly;
        }

        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return DataProviderProbeResult.Ready("The SQLite target answered a read-only query.");
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
        where TKey : notnull => new SqliteRepository<TEntity, TKey>(services, ResolveRoute(services, source), this);

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
