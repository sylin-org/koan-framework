using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Weaviate;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "Weaviate",
    ContainerImage = "cr.weaviate.io/semitechnologies/weaviate", DefaultTag = "1.37.6",
    DefaultPorts = new[] { 8080 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true", "session-visibility=true" },
    Env = new[]
    {
        "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true",
        "AUTOSCHEMA_ENABLED=false",
        "ASYNC_INDEXING=false",
        "DEFAULT_VECTORIZER_MODULE=none",
        "PERSISTENCE_DATA_PATH=/var/lib/weaviate",
        "CLUSTER_HOSTNAME=node1",
        "RAFT_BOOTSTRAP_EXPECT=1"
    },
    Volumes = new[] { "./Data/weaviate-1.37:/var/lib/weaviate" },
    AppEnv = new[] { "Koan__Data__Weaviate__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = Infrastructure.Constants.ReadyPath,
    HealthIntervalSeconds = 5, HealthTimeoutSeconds = 3, HealthRetries = 12,
    Scheme = "http", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 8080,
    UriPattern = "http://{host}:{port}", LocalScheme = "http", LocalHost = "localhost",
    LocalPort = 8080, LocalPattern = "http://{host}:{port}")]
public sealed class WeaviateVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<WeaviateOptions> options) : IVectorAdapterFactory
{
    private readonly WeaviateOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.FullNamespace,
        Separator = "_",
        Casing = NameCasing.AsIs,
        PartitionSeparator = '_',
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = false, AllowedExtraChars = "-_" }
    };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        VectorSpacePlan plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Visibility != VectorVisibility.Session)
            throw new NotSupportedException(
                "Weaviate realizes awaited Session visibility and does not simulate Eventual visibility.");
        var http = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory
            ?? throw new InvalidOperationException("Weaviate requires IHttpClientFactory. Reference the connector and call AddKoan().");
        return new WeaviateRepository<TEntity, TKey>(
            services, this, plan, ResolveRoute(plan.Source), _options, http);
    }

    internal WeaviateRoute ResolveRoute(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var endpoint = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, sources, Provider, name, _options.Endpoint, this);
        var apiKey = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "ApiKey", _options.ApiKey ?? string.Empty, this);
        return WeaviateRoute.Create(name, endpoint, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            sources.GetPlan(name, Provider));
    }

    private static WeaviateOptions Validate(WeaviateOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = WeaviateRoute.NormalizeEndpoint(value.Endpoint);
        if (value.TimeoutSeconds <= 0) throw Invalid(nameof(value.TimeoutSeconds));
        if (value.VisibilityTimeoutSeconds <= 0) throw Invalid(nameof(value.VisibilityTimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxClearPoints <= 0) throw Invalid(nameof(value.MaxClearPoints));
        if (value.MaxSearchCandidates <= 1) throw Invalid(nameof(value.MaxSearchCandidates));
        if (value.MaxResponseBytes <= 0) throw Invalid(nameof(value.MaxResponseBytes));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"WeaviateOptions.{name} must be greater than zero.");
}
