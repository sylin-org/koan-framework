using Koan.Core.Capabilities;
using Koan.Core.Services;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.Mongo.Infrastructure;
using Koan.Data.Connector.Mongo.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo;

[ProviderPriority(Constants.Provider.Priority)]
[KoanService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo", DefaultTag = "8.3.4", DefaultPorts = [27017],
    Capabilities = ["protocol=mongodb"], Volumes = ["./Data/mongo-8.3:/data/db"],
    AppEnv = ["Koan__Data__Mongo__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Mongo__Database=Koan"],
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
public sealed class MongoAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => [Constants.Provider.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Mongo"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider.Name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Constants.Provider.Alias, StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => MongoFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers |
        SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer |
        SourceInspectionCapabilities.SampleRecords,
        ["pipeline"]);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source) =>
        new MongoSourceIntegration(
            ResolveRoute(services, source),
            services.GetRequiredService<MongoClientManager>());

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        var mapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        return new MongoRepository<TEntity, TKey>(
            services,
            this,
            route,
            services.GetRequiredService<MongoClientManager>(),
            mapping);
    }

    internal MongoRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            registry,
            Provider,
            resolvedSource,
            defaults.ConnectionString,
            this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            configuration,
            registry,
            Provider,
            resolvedSource,
            "Database",
            defaults.Database,
            this);
        var definition = registry.GetSource(resolvedSource);
        return new MongoRoute(
            resolvedSource,
            connection,
            database,
            definition?.StorageLifecycle ?? StorageLifecycle.Managed,
            definition?.Access ?? DataSourceAccess.ReadWrite);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
            NameOverride = options.CollectionName,
            MaxIdentifierBytes = 190
        };
    }
}

internal sealed record MongoRoute(
    string Source,
    string ConnectionString,
    string Database,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access);
