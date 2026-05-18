using System.Text;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(System.Type, string?), string> _nameCache = new();

    public string Provider => "weaviate";

    public bool CanHandle(string provider) => string.Equals(provider, "weaviate", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<WeaviateOptions>?)sp.GetService(typeof(IOptions<WeaviateOptions>))
            ?? throw new InvalidOperationException("WeaviateOptions not configured; bind Koan:Data:Weaviate.");
        return new WeaviateVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }

    // Weaviate uses '_' (GraphQL-compliant) rather than '#' as the partition separator —
    // GraphQL identifiers don't allow '#'. Vector class names use FullNamespace + '_' so they
    // remain valid GraphQL types.
    public string ResolveStorage(Type entityType, string? partition, IServiceProvider services)
    {
        var trimmed = partition?.Trim();
        var cacheKey = (entityType, string.IsNullOrEmpty(trimmed) ? null : trimmed);
        return _nameCache.GetOrAdd(cacheKey, _ =>
        {
            var convention = new StorageNameResolver.Convention(
                StorageNamingStyle.FullNamespace,
                "_",
                NameCasing.AsIs);
            var name = StorageNameResolver.Resolve(entityType, convention).Trim();

            if (string.IsNullOrEmpty(trimmed)) return name;

            var concrete = Guid.TryParse(trimmed, out var guid)
                ? guid.ToString("D").Replace("-", "_")
                : SanitizeForGraphQL(trimmed);
            return name + "_" + concrete;
        });
    }

    private static string SanitizeForGraphQL(string partition)
    {
        var sanitized = new StringBuilder(partition.Length);
        for (int i = 0; i < partition.Length; i++)
        {
            var c = partition[i];
            if (i == 0)
            {
                // First char must be letter or underscore
                if (char.IsLetter(c) || c == '_')
                    sanitized.Append(c);
                else
                    sanitized.Append('_');
            }
            else
            {
                // Subsequent chars: alphanumeric or underscore
                if (char.IsLetterOrDigit(c) || c == '_')
                    sanitized.Append(c);
                else
                    sanitized.Append('_');
            }
        }
        return sanitized.ToString();
    }
}

