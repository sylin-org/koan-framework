
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Vector.Connector.Milvus;

[ProviderPriority(30)]
[KoanService(ServiceKind.Vector, shortCode: "milvus", name: "Milvus",
    ContainerImage = "milvusdb/milvus",
    DefaultTag = "2.4.0",
    DefaultPorts = new[] { 19530 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true" },
    Env = new[]
    {
        "ETCD_USE_EMBED=true",
        "COMMON_MAXPROCS=2"
    },
    Volumes = new[] { "./Data/milvus:/var/lib/milvus" },
    AppEnv = new[] { "Koan__Data__Milvus__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/v2/health",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 3,
    HealthRetries = 12,
    Scheme = "http", Host = "milvus", EndpointPort = 19530, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 19530, LocalPattern = "http://{host}:{port}")]
public sealed class MilvusVectorAdapterFactory : IVectorAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "milvus", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<MilvusOptions>?)sp.GetService(typeof(IOptions<MilvusOptions>))
            ?? throw new InvalidOperationException("MilvusOptions not configured; bind Koan:Data:Milvus.");
        return new MilvusVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }
}

