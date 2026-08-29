using FirebirdSql.Data.FirebirdClient;
using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Firebird.Infrastructure;
using Koan.Data.Connector.Firebird.Runtime;
using Koan.Data.Core;
using Koan.Data.Relational;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Firebird;

[ProviderPriority(14)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "Firebird",
    ContainerImage = "firebirdsql/firebird", DefaultTag = "5.0.4",
    DefaultPorts = [3050], Capabilities = ["protocol=firebird"],
    Env = ["FIREBIRD_ROOT_PASSWORD", "FIREBIRD_DATABASE"], Volumes = ["./Data/firebird:/var/lib/firebird/data"],
    AppEnv = ["Koan__Data__Firebird__ConnectionString=firebird://{host}:{port}"],
    Scheme = "firebird", Host = "firebird", EndpointPort = 3050, UriPattern = "firebird://{host}:{port}",
    LocalScheme = "firebird", LocalHost = "localhost", LocalPort = 3050, LocalPattern = "firebird://{host}:{port}")]
public sealed class FirebirdAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Firebird"];

    public void DescribeClaims(IDataClaims claims) => FirebirdFeatures.Declare(claims);

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
            lane => new FbConnection(route.ReadLanes[lane]),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                // Read lanes are enforced by the engine: a read-only transaction rejects the first write.
                var options = new FbTransactionOptions
                {
                    TransactionBehavior = FbTransactionBehavior.Concurrency | FbTransactionBehavior.Read,
                    WaitTimeout = TimeSpan.FromSeconds(5)
                };
                return await ((FbConnection)connection).BeginTransactionAsync(options, ct).ConfigureAwait(false);
            },
            new FirebirdInspector(route));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        return new FirebirdRepository<TEntity, TKey>(services, new FirebirdRepositoryOptions
        {
            ConnectionString = route.ConnectionString,
            Source = route.Source,
            Database = FirebirdConnectionStrings.Normalize(route.ConnectionString).Database,
            DdlPolicy = route.Options.DdlPolicy,
            SchemaMatching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            SourcePlan = route.Policy
        });
    }

    internal FirebirdRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<FirebirdOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.ConnectionString, this);
        if (string.IsNullOrWhiteSpace(connection) ||
            string.Equals(connection.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Firebird source '{resolvedSource}' could not resolve its automatic endpoint. Configure ConnectionStrings:{resolvedSource} or Koan:Data:Firebird:ConnectionString.");
        var builder = FirebirdConnectionStrings.Normalize(connection);
        var configuredDatabase = AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, nameof(FirebirdOptions.Database), options.Database, this);
        if (!string.IsNullOrWhiteSpace(configuredDatabase) &&
            !string.Equals(configuredDatabase, Constants.DefaultDatabase, StringComparison.Ordinal))
            builder.Database = configuredDatabase;
        if (string.IsNullOrWhiteSpace(builder.Database))
            throw new InvalidOperationException($"Firebird source '{resolvedSource}' does not select a database. Configure Database or include it in the connection string.");
        connection = builder.ConnectionString;
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(static lane => lane.Key, static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new FirebirdRoute(
            resolvedSource,
            connection,
            options,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<FirebirdOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = Infrastructure.Constants.MaxIdentifierBytes
        };
    }
}
