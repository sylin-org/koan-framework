using System.Text;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Core.Services;

namespace Koan.Data.Vector.Connector.Weaviate;

[ProviderPriority(10)]
[KoanService(ServiceKind.Vector, shortCode: "weaviate", name: "Weaviate",
    ContainerImage = "semitechnologies/weaviate",
    DefaultTag = "1.25.6",
    DefaultPorts = new[] { 8080 },
    Capabilities = new[] { "protocol=http", "vector-search=true" },
    Env = new[]
    {
        "QUERY_DEFAULTS_LIMIT=25",
        "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true",
        "PERSISTENCE_DATA_PATH=/var/lib/weaviate",
        "DEFAULT_VECTORIZER_MODULE=none",
        "CLUSTER_HOSTNAME=node1",
        "RAFT_BOOTSTRAP_EXPECT=1"
    },
    Volumes = new[] { "./Data/weaviate:/var/lib/weaviate" },
    AppEnv = new[] { "Koan__Data__Weaviate__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/v1/.well-known/ready",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "weaviate", EndpointPort = 8080, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 8080, LocalPattern = "http://{host}:{port}")]
public sealed class WeaviateVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "weaviate";

    // ARCH-0103 §4.1: accepts the routed source for contract alignment; per-source physical placement (native
    // multi-tenancy / per-cluster) is realized in P4. Until then a Database-mode route resolves but is not yet honored here.
    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<WeaviateOptions>?)sp.GetService(typeof(IOptions<WeaviateOptions>))
            ?? throw new InvalidOperationException("WeaviateOptions not configured; bind Koan:Data:Weaviate.");
        return new WeaviateVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }

    // Weaviate class names are GraphQL types: FullNamespace + '_' separator, and the partition uses '_'
    // (GraphQL identifiers don't allow '#'). The partition token keeps only [A-Za-z0-9_] (the GUID "D"
    // form's hyphens fold to '_'); the leading char is always a letter because the class name precedes it.
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.FullNamespace,
            Separator = "_",
            Casing = NameCasing.AsIs,
            PartitionSeparator = '_',
            Partition = new PartitionTokenPolicy { GuidFormat = "D", AllowedExtraChars = "_" },
        };
}

