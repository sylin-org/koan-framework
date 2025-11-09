using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.OpenSearch;

[ProviderPriority(20)]
[KoanService(ServiceKind.Vector, shortCode: "opensearch", name: "OpenSearch",
    ContainerImage = "opensearchproject/opensearch",
    DefaultTag = "2.13.0",
    DefaultPorts = new[] { 9200 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true" },
    Env = new[]
    {
        "discovery.type=single-node",
        "DISABLE_SECURITY_PLUGIN=true",
        "OPENSEARCH_JAVA_OPTS=-Xms512m -Xmx512m"
    },
    Volumes = new[] { "./Data/opensearch:/usr/share/opensearch/data" },
    AppEnv = new[] { "Koan__Data__OpenSearch__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/_cluster/health",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "opensearch", EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class OpenSearchVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "opensearch";

    public bool CanHandle(string provider)
        => string.Equals(provider, "opensearch", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(provider, "elastic", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<OpenSearchOptions>?)sp.GetService(typeof(IOptions<OpenSearchOptions>))
            ?? throw new InvalidOperationException("OpenSearchOptions not configured; bind Koan:Data:OpenSearch.");
        return new OpenSearchVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }

    // INamingProvider implementation
    public string RepositorySeparator => "-";  // OpenSearch uses hyphens in index names

    public string GetStorageName(Type entityType, IServiceProvider services)
    {
        var opts = services.GetService<IOptions<OpenSearchOptions>>()?.Value;
        var separator = opts?.IndexPrefix is not null ? "-" : "_";
        var convention = new StorageNameResolver.Convention(
            StorageNamingStyle.EntityType,
            separator,
            NameCasing.Lower);

        return StorageNameResolver.Resolve(entityType, convention);
    }

    public string GetConcretePartition(string partition)
    {
        // OpenSearch: Lowercase and sanitize for index names
        if (Guid.TryParse(partition, out var guid))
            return guid.ToString("N");  // Lowercase, no hyphens

        // Named partitions: lowercase
        return partition.ToLowerInvariant();
    }
}

