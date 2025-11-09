using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.ElasticSearch;

[ProviderPriority(20)]
[KoanService(ServiceKind.Vector, shortCode: "elasticsearch", name: "Elasticsearch",
    ContainerImage = "docker.elastic.co/elasticsearch/elasticsearch",
    DefaultTag = "8.13.4",
    DefaultPorts = new[] { 9200 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true" },
    Env = new[]
    {
        "discovery.type=single-node",
        "xpack.security.enabled=false",
        "ES_JAVA_OPTS=-Xms512m -Xmx512m"
    },
    Volumes = new[] { "./Data/elasticsearch:/usr/share/elasticsearch/data" },
    AppEnv = new[] { "Koan__Data__ElasticSearch__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/_cluster/health",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "elasticsearch", EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class ElasticSearchVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "elasticsearch";

    public bool CanHandle(string provider)
        => string.Equals(provider, "elasticsearch", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(provider, "elastic", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<ElasticSearchOptions>?)sp.GetService(typeof(IOptions<ElasticSearchOptions>))
            ?? throw new InvalidOperationException("ElasticSearchOptions not configured; bind Koan:Data:ElasticSearch.");
        return new ElasticSearchVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }

    // INamingProvider implementation
    public string RepositorySeparator => "-";  // Elasticsearch uses hyphens in index names

    public string GetStorageName(Type entityType, IServiceProvider services)
    {
        var opts = services.GetService<IOptions<ElasticSearchOptions>>()?.Value;
        var separator = opts?.IndexPrefix is not null ? "-" : "_";
        var convention = new StorageNameResolver.Convention(
            StorageNamingStyle.EntityType,
            separator,
            NameCasing.Lower);

        return StorageNameResolver.Resolve(entityType, convention);
    }

    public string GetConcretePartition(string partition)
    {
        // Elasticsearch: Lowercase and sanitize for index names
        if (Guid.TryParse(partition, out var guid))
            return guid.ToString("N");  // Lowercase, no hyphens

        // Named partitions: lowercase
        return partition.ToLowerInvariant();
    }
}

