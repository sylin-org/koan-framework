using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Chroma;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "Chroma",
    ContainerImage = "chromadb/chroma", DefaultTag = "1.5.9",
    DefaultPorts = new[] { 8000 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=scalar", "session-visibility=true" },
    Volumes = new[] { "./Data/chroma-1.5:/data" },
    AppEnv = new[] { "Koan__Data__Chroma__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = Infrastructure.Constants.HeartbeatPath,
    HealthIntervalSeconds = 5, HealthTimeoutSeconds = 3, HealthRetries = 12,
    Scheme = "http", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 8000,
    UriPattern = "http://{host}:{port}", LocalScheme = "http", LocalHost = "localhost",
    LocalPort = 8000, LocalPattern = "http://{host}:{port}")]
public sealed class ChromaVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<ChromaOptions> options) : IVectorAdapterFactory
{
    private readonly ChromaOptions _options = Validate(options.Value);

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
                "Chroma realizes awaited Session visibility with synchronous writes and does not simulate Eventual visibility.");
        var http = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory
            ?? throw new InvalidOperationException("Chroma requires IHttpClientFactory. Reference the connector and call AddKoan().");
        return new ChromaRepository<TEntity, TKey>(
            services, this, plan, ResolveRoute(plan.Source), _options, http);
    }

    internal ChromaRoute ResolveRoute(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var endpoint = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, sources, Provider, name, _options.Endpoint, this);
        var apiKey = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "ApiKey", _options.ApiKey ?? string.Empty, this);
        var tenant = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "Tenant", _options.Tenant, this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "Database", _options.Database, this);
        return ChromaRoute.Create(name, endpoint, tenant, database,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey, sources.GetPlan(name, Provider));
    }

    private static ChromaOptions Validate(ChromaOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = ChromaRoute.NormalizeEndpoint(value.Endpoint);
        if (value.TimeoutSeconds <= 0) throw Invalid(nameof(value.TimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxSearchCandidates <= 1) throw Invalid(nameof(value.MaxSearchCandidates));
        if (value.MaxResponseBytes <= 0) throw Invalid(nameof(value.MaxResponseBytes));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"ChromaOptions.{name} must be greater than zero.");
}
