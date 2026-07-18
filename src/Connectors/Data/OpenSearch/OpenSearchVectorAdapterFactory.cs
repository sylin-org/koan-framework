using Koan.Core;
using Koan.Core.Services;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Id, name: "OpenSearch",
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
    Scheme = "http", Host = Infrastructure.Constants.Provider.Id, EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class OpenSearchVectorAdapterFactory : SearchEngineVectorAdapterFactory<OpenSearchOptions>
{
    protected override SearchEngineConnectorDescriptor Descriptor => Infrastructure.Constants.Descriptor;
}
