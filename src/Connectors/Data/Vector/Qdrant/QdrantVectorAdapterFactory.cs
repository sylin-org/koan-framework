using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Core.Services;

namespace Koan.Data.Vector.Connector.Qdrant;

[ProviderPriority(30)]
[KoanService(ServiceKind.Vector, shortCode: "qdrant", name: "Qdrant",
    ContainerImage = "qdrant/qdrant",
    DefaultTag = "v1.10.0",
    DefaultPorts = new[] { 6333, 6334 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true", "synchronous-writes=true", "profile=lean", "quantization=scalar-default" },
    Volumes = new[] { "./Data/qdrant:/qdrant/storage" },
    AppEnv = new[] { "Koan__Data__Qdrant__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/readyz",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 3,
    HealthRetries = 12,
    Scheme = "http", Host = "qdrant", EndpointPort = 6333, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 6333, LocalPattern = "http://{host}:{port}")]
public sealed class QdrantVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "qdrant";

    // ARCH-0103 §4.1: accepts the routed source for contract alignment; per-source physical placement (native
    // multi-tenancy / per-collection) is realized in P4.
    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<QdrantOptions>?)sp.GetService(typeof(IOptions<QdrantOptions>))
            ?? throw new InvalidOperationException("QdrantOptions not configured; bind Koan:Data:Qdrant.");
        return new QdrantVectorRepository<TEntity, TKey>(httpFactory, options, sp, this, source);
    }

    // Qdrant collection names accept [A-Za-z0-9_-] and reject '#', so the partition separator is '_' (as
    // Milvus settled on); names are lowercased EntityType.
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Separator = "_",
            Casing = NameCasing.Lower,
            PartitionSeparator = '_',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true, AllowedExtraChars = "-_" },
        };
}
