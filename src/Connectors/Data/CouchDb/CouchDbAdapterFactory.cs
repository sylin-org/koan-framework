using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.CouchDb.Infrastructure;
using Koan.Data.Connector.CouchDb.Runtime;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.CouchDb;

[ProviderPriority(15)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "CouchDB",
    ContainerImage = "couchdb", DefaultTag = "3.5", DefaultPorts = [5984],
    Capabilities = ["protocol=http"], Env = ["COUCHDB_USER", "COUCHDB_PASSWORD"],
    Volumes = ["./Data/couchdb:/opt/couchdb/data"],
    AppEnv = ["Koan__Data__CouchDb__Endpoint=http://{host}:{port}"],
    HealthEndpoint = "/_up", HealthIntervalSeconds = 5, HealthTimeoutSeconds = 2, HealthRetries = 12,
    Scheme = "http", Host = "couchdb", EndpointPort = 5984, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 5984, LocalPattern = "http://{host}:{port}")]
public sealed class CouchDbAdapterFactory : IDataAdapterFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.CouchDb"];

    public void DescribeClaims(IDataClaims claims) => CouchDbFeatures.Declare(claims);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        var mapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        return new CouchDbRepository<TEntity, TKey>(
            services,
            route,
            services.GetRequiredService<CouchDbClientManager>(),
            mapping);
    }

    internal CouchDbRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var options = services.GetRequiredService<IOptions<CouchDbOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;

        // The endpoint rides the source's connection string (a couchdb:// URI or an http(s) URL);
        // credentials may come from the connection string, source settings, or adapter options.
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, options.Endpoint, this);
        if (string.IsNullOrWhiteSpace(connection) || IsAuto(connection))
            throw new InvalidOperationException(
                $"CouchDB source '{resolvedSource}' could not resolve its endpoint. Configure ConnectionStrings:{resolvedSource} or Koan:Data:CouchDb:Endpoint.");
        var (endpoint, userId, password) = Split(connection);
        // Credentials may also live in source-scoped or adapter options, keyed apart from the endpoint.
        userId ??= AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, nameof(CouchDbOptions.UserId), options.UserId, this);
        password ??= AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource, nameof(CouchDbOptions.Password), options.Password, this);

        var prefix = AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, resolvedSource,
            nameof(CouchDbOptions.Database), options.Database, this);
        if (string.IsNullOrWhiteSpace(prefix)) prefix = Constants.DefaultDatabase;

        return new CouchDbRoute(
            resolvedSource,
            endpoint,
            userId,
            password,
            prefix,
            registry.GetPlan(resolvedSource, Provider, endpoint));

        static bool IsAuto(string value) => value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);

        static (string Endpoint, string? User, string? Password) Split(string value)
        {
            var (endpoint, user, password) = CouchDbEndpoint.Parse(value);
            return (endpoint.ToString().TrimEnd('/'), user, password);
        }
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchDbOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
            MaxIdentifierBytes = 128
        };
    }
}
