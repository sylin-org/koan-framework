using Koan.Core;
using Koan.Core.Services;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.ElasticSearch;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Id, name: "Elasticsearch",
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
    Scheme = "http", Host = Infrastructure.Constants.Provider.Id, EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class ElasticSearchVectorAdapterFactory : SearchEngineVectorAdapterFactory<ElasticSearchOptions>
{
    protected override SearchEngineConnectorDescriptor Descriptor => Infrastructure.Constants.Descriptor;
}
