using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.SqlServer.Infrastructure;
using Koan.Data.Connector.SqlServer.Runtime;
using Koan.Data.Relational;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.SqlServer;

[ProviderPriority(15)]
[KoanService(ServiceKind.Database, shortCode: Constants.Service, name: "SQL Server",
    ContainerImage = "mcr.microsoft.com/mssql/server", DefaultTag = "2025-CU6-GDR1-ubuntu-24.04",
    DefaultPorts = [1433], Capabilities = ["protocol=mssql"],
    Env = ["ACCEPT_EULA=Y", "MSSQL_SA_PASSWORD"], Volumes = ["./Data/mssql-2025:/var/opt/mssql"],
    AppEnv = ["Koan__Data__SqlServer__ConnectionString={scheme}://{host}:{port}"],
    Scheme = "mssql", Host = "mssql", EndpointPort = 1433, UriPattern = "mssql://{host}:{port}",
    LocalScheme = "mssql", LocalHost = "localhost", LocalPort = 1433, LocalPattern = "mssql://{host}:{port}")]
public sealed class SqlServerAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [Constants.Service, "microsoft.sqlserver"];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.SqlServer"];

    public void DescribeClaims(IDataClaims claims) => SqlServerFeatures.Declare(claims);

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
            lane => new SqlConnection(route.ReadLanes[lane]),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static (connection, ct) => connection.BeginTransactionAsync(ct).AsTask(),
            new SqlServerInspector(route));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        return new SqlServerRepository<TEntity, TKey>(services, new SqlServerRepositoryOptions
        {
            ConnectionString = route.ConnectionString,
            Source = route.Source,
            Schema = route.Schema,
            DdlPolicy = route.Options.DdlPolicy,
            SchemaMatching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            SourcePlan = route.Plan
        });
    }

    internal SqlServerRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.ConnectionString, this);
        var schema = AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, "Schema", options.Schema, this);
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(
                static lane => lane.Key,
                static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new SqlServerRoute(
            resolvedSource, connection, schema, options,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 128
        };
    }
}

internal sealed record SqlServerRoute(
    string Source,
    string ConnectionString,
    string Schema,
    SqlServerOptions Options,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes);
