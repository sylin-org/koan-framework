using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.MySql.Infrastructure;
using Koan.Data.Connector.MySql.Runtime;
using Koan.Data.Relational;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Koan.Data.Connector.MySql;

[ProviderPriority(13)]
[KoanService(ServiceKind.Database, shortCode: Constants.Service, name: "MySQL",
    ContainerImage = "mysql", DefaultTag = "8.4",
    DefaultPorts = [3306], Capabilities = ["protocol=mysql"],
    Env = ["MYSQL_ROOT_PASSWORD", "MYSQL_DATABASE"], Volumes = ["./Data/mysql:/var/lib/mysql"],
    AppEnv = ["Koan__Data__MySql__ConnectionString={scheme}://{host}:{port}"],
    Scheme = "mysql", Host = "mysql", EndpointPort = 3306, UriPattern = "mysql://{host}:{port}",
    LocalScheme = "mysql", LocalHost = "localhost", LocalPort = 3306, LocalPattern = "mysql://{host}:{port}")]
public sealed class MySqlAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.MySql"];

    public void DescribeClaims(IDataClaims claims) => MySqlFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords,
        ["sql"],
        enforcesReadLanes: true);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        return new RelationalSourceIntegration(
            lane => new MySqlConnection(route.ReadLanes[lane]),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SET TRANSACTION READ ONLY";
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            },
            new MySqlInspector(route));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        return new MySqlRepository<TEntity, TKey>(services, new MySqlRepositoryOptions
        {
            ConnectionString = route.ConnectionString,
            Source = route.Source,
            Database = route.Database,
            DdlPolicy = route.Options.DdlPolicy,
            SchemaMatching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            SourcePlan = route.Plan
        });
    }

    internal MySqlRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<MySqlOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.ConnectionString, this);
        if (string.IsNullOrWhiteSpace(connection) ||
            string.Equals(connection.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"MySQL source '{resolvedSource}' could not resolve its automatic endpoint. Configure ConnectionStrings:{resolvedSource} or Koan:Data:MySql:ConnectionString.");
        var builder = MySqlConnectionStrings.Normalize(connection);
        var configuredDatabase = ResolveDatabase(
            configuration, registry, resolvedSource, builder.Database, options.Database);
        builder.Database = configuredDatabase;
        connection = builder.ConnectionString;
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException($"MySQL source '{resolvedSource}' does not select a database. Configure Database or include it in the connection string.");
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(
                static lane => lane.Key,
                lane => NormalizeReadLane(lane.Value.ConnectionString, database, resolvedSource, lane.Key),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new MySqlRoute(
            resolvedSource, connection, database, options,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MySqlOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 64
        };
    }

    private static string NormalizeReadLane(string connection, string database, string source, string lane)
    {
        if (string.Equals(connection.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"MySQL read lane '{lane}' on source '{source}' must resolve to a concrete connection string.");
        var builder = MySqlConnectionStrings.Normalize(connection);
        if (string.IsNullOrWhiteSpace(builder.Database)) builder.Database = database;
        return builder.ConnectionString;
    }

    private string ResolveDatabase(
        IConfiguration configuration,
        DataSourceRegistry registry,
        string source,
        string connectionDatabase,
        string defaultDatabase)
    {
        var fallback = string.IsNullOrWhiteSpace(connectionDatabase) ? defaultDatabase : connectionDatabase;
        if (string.Equals(source, Constants.DefaultSource, StringComparison.OrdinalIgnoreCase))
            return AdapterConnectionResolver.GetSourceSetting(
                configuration, registry, Provider, source, nameof(MySqlOptions.Database), fallback, this);

        var definition = registry.GetSource(source);
        var sourceBelongsToProvider = string.IsNullOrWhiteSpace(definition?.Adapter) ||
                                      string.Equals(definition.Adapter, Provider, StringComparison.OrdinalIgnoreCase) ||
                                      Aliases.Contains(definition.Adapter, StringComparer.OrdinalIgnoreCase);

        // A source-scoped database decision is more specific than the database carried by its connection.
        if (sourceBelongsToProvider &&
            definition?.Settings.TryGetValue(nameof(MySqlOptions.Database), out var sourceDatabase) == true)
            return sourceDatabase;

        var providerDatabase = configuration[
            $"Koan:Data:Sources:{source}:{Provider}:{nameof(MySqlOptions.Database)}"];
        if (!string.IsNullOrWhiteSpace(providerDatabase)) return providerDatabase;

        // A concrete named-source connection is itself a database-placement decision. Do not overwrite it with the
        // adapter-wide Database setting; that would collapse otherwise isolated sources onto the default database.
        if (HasConcreteNamedConnection(configuration, definition, source, sourceBelongsToProvider) &&
            !string.IsNullOrWhiteSpace(connectionDatabase))
            return connectionDatabase;

        return AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, source, nameof(MySqlOptions.Database), fallback, this);
    }

    private bool HasConcreteNamedConnection(
        IConfiguration configuration,
        DataSourceRegistry.SourceDefinition? definition,
        string source,
        bool sourceBelongsToProvider)
    {
        if (sourceBelongsToProvider && IsConcrete(definition?.ConnectionString)) return true;
        if (IsConcrete(configuration[$"Koan:Data:Sources:{source}:{Provider}:ConnectionString"])) return true;
        return sourceBelongsToProvider && IsConcrete(configuration.GetConnectionString(source));
    }

    private static bool IsConcrete(string? connection) =>
        !string.IsNullOrWhiteSpace(connection) &&
        !string.Equals(connection.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}

internal sealed record MySqlRoute(
    string Source,
    string ConnectionString,
    string Database,
    MySqlOptions Options,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes);
