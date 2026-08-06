using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Qdrant;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "Qdrant",
    ContainerImage = "qdrant/qdrant", DefaultTag = "v1.18.3",
    DefaultPorts = new[] { 6333, 6334 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true", "session-visibility=true" },
    Volumes = new[] { "./Data/qdrant-1.18:/qdrant/storage" },
    AppEnv = new[] { "Koan__Data__Qdrant__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = Infrastructure.Constants.ReadyPath,
    HealthIntervalSeconds = 5, HealthTimeoutSeconds = 3, HealthRetries = 12,
    Scheme = "http", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 6333,
    UriPattern = "http://{host}:{port}", LocalScheme = "http", LocalHost = "localhost",
    LocalPort = 6333, LocalPattern = "http://{host}:{port}")]
public sealed class QdrantVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<QdrantOptions> options) : IVectorAdapterFactory
{
    private readonly QdrantOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
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
                "Qdrant realizes awaited Session visibility with wait=true and does not simulate Eventual visibility.");
        var http = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory
            ?? throw new InvalidOperationException("Qdrant requires IHttpClientFactory. Reference the connector and call AddKoan().");
        return new QdrantRepository<TEntity, TKey>(
            services, this, plan, ResolveRoute(plan.Source), _options, http);
    }

    internal QdrantRoute ResolveRoute(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var endpoint = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, sources, Provider, name, _options.Endpoint, this);
        var apiKey = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "ApiKey", _options.ApiKey ?? string.Empty, this);
        return QdrantRoute.Create(name, endpoint, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            sources.GetPlan(name, Provider));
    }

    private static QdrantOptions Validate(QdrantOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = QdrantRoute.NormalizeEndpoint(value.Endpoint);
        if (value.TimeoutSeconds <= 0) throw Invalid(nameof(value.TimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxSearchCandidates <= 1) throw Invalid(nameof(value.MaxSearchCandidates));
        if (value.MaxResponseBytes <= 0) throw Invalid(nameof(value.MaxResponseBytes));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"QdrantOptions.{name} must be greater than zero.");
}
