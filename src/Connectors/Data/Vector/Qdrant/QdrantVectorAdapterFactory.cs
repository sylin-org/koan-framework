using System.Text;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string?), string> _nameCache = new();

    public string Provider => "qdrant";

    public bool CanHandle(string provider)
        => string.Equals(provider, "qdrant", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<QdrantOptions>?)sp.GetService(typeof(IOptions<QdrantOptions>))
            ?? throw new InvalidOperationException("QdrantOptions not configured; bind Koan:Data:Qdrant.");
        return new QdrantVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }

    public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
    {
        var trimmed = partition?.Trim();
        var cacheKey = (entityType, string.IsNullOrEmpty(trimmed) ? null : trimmed);
        return _nameCache.GetOrAdd(cacheKey, _ =>
        {
            var convention = new StorageNameResolver.Convention(
                StorageNamingStyle.EntityType,
                "_",
                NameCasing.Lower);
            var name = StorageNameResolver.Resolve(entityType, convention).Trim();

            if (string.IsNullOrEmpty(trimmed)) return name;

            // Qdrant collection names accept letters, digits, hyphens, underscores. The `#`
            // separator the other Koan adapters use is rejected; we use `_` matching what
            // Milvus settled on. Partition values are sanitized to the same character set.
            var concrete = Guid.TryParse(trimmed, out var guid)
                ? guid.ToString("N")
                : SanitizeForQdrant(trimmed);
            return name + "_" + concrete;
        });
    }

    private static string SanitizeForQdrant(string partition)
    {
        var sanitized = new StringBuilder(partition.Length);
        foreach (var c in partition.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        return sanitized.ToString();
    }
}
