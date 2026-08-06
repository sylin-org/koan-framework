using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.Cockroach.Infrastructure;
using Koan.Data.Connector.Cockroach.Runtime;
using Koan.Data.Relational;
using Koan.Data.Relational.Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Koan.Data.Connector.Cockroach;

[ProviderPriority(13)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "CockroachDB",
    ContainerImage = "cockroachdb/cockroach", DefaultTag = "v26.2.3", DefaultPorts = [26257],
    Capabilities = ["protocol=postgres"],
    Volumes = ["./Data/cockroach-26.2:/cockroach/cockroach-data"],
    AppEnv = ["Koan__Data__Cockroach__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Cockroach__Database=Koan"],
    Scheme = "cockroach", Host = "cockroach", EndpointPort = 26257, UriPattern = "cockroach://{host}:{port}",
    LocalScheme = "cockroach", LocalHost = "localhost", LocalPort = 26257, LocalPattern = "cockroach://{host}:{port}")]
public sealed class CockroachAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [Constants.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Cockroach"];

    public void DescribeClaims(IDataClaims claims) => NpgsqlFeatures.Declare(claims);

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
            lane => new NpgsqlConnection(route.ReadLanes[lane]),
            route.ReadLanes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            static async (connection, ct) =>
            {
                var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "SET TRANSACTION READ ONLY";
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    return transaction;
                }
                catch
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
            new CockroachInspector(route));
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        return new NpgsqlRepository<TEntity, TKey>(services, new NpgsqlRepositoryOptions
        {
            ProviderName = Provider,
            ConnectionString = route.ConnectionString,
            Source = route.Source,
            SearchPath = route.SearchPath,
            NamingStyle = route.Options.NamingStyle,
            Separator = route.Options.Separator,
            DdlPolicy = route.Options.DdlPolicy,
            SchemaMatching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            StableOrder = NpgsqlStableOrder.Identity,
            SourcePlan = route.Plan
        }, services.GetRequiredService<IStorageNameResolver>());
    }

    internal CockroachRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<CockroachOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.ConnectionString, this);
        var searchPath = AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, "SearchPath", options.SearchPath, this);
        var readLanes = registry.GetSource(resolvedSource)?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(
                static lane => lane.Key,
                static lane => lane.Value.ConnectionString,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new CockroachRoute(
            resolvedSource,
            connection,
            searchPath,
            options,
            registry.GetPlan(resolvedSource, Provider, connection),
            readLanes);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CockroachOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 63
        };
    }
}

internal sealed record CockroachRoute(
    string Source,
    string ConnectionString,
    string SearchPath,
    CockroachOptions Options,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes);
