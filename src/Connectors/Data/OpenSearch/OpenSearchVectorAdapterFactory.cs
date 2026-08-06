using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.OpenSearch;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "OpenSearch",
    ContainerImage = "opensearchproject/opensearch", DefaultTag = "3.7.0",
    DefaultPorts = new[] { 9200 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true", "session-visibility=true" },
    Env = new[] { "discovery.type=single-node", "DISABLE_SECURITY_PLUGIN=true", "OPENSEARCH_JAVA_OPTS=-Xms512m -Xmx512m" },
    Volumes = new[] { "./Data/opensearch-3.7:/usr/share/opensearch/data" },
    AppEnv = new[] { "Koan__Data__OpenSearch__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = Infrastructure.Constants.HealthPath,
    HealthIntervalSeconds = 5, HealthTimeoutSeconds = 3, HealthRetries = 12,
    Scheme = "http", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 9200,
    UriPattern = "http://{host}:{port}", LocalScheme = "http", LocalHost = "localhost",
    LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class OpenSearchVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<OpenSearchOptions> options) : IVectorAdapterFactory
{
    private readonly OpenSearchOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Separator = "-",
        Casing = NameCasing.AsIs,
        PartitionSeparator = '-',
        MaxIdentifierBytes = 220,
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = false, AllowedExtraChars = "-._" }
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
                "OpenSearch realizes awaited Session visibility with an explicit refresh and does not simulate Eventual visibility.");
        var http = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory
            ?? throw new InvalidOperationException("OpenSearch requires IHttpClientFactory. Reference the connector and call AddKoan().");
        return new OpenSearchRepository<TEntity, TKey>(
            services, this, plan, ResolveRoute(plan.Source), _options, http);
    }

    internal OpenSearchRoute ResolveRoute(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var endpoint = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, sources, Provider, name, _options.Endpoint, this);
        return OpenSearchRoute.Create(
            name,
            endpoint,
            Setting(name, "ApiKey", _options.ApiKey),
            Setting(name, "Username", _options.Username),
            Setting(name, "Password", _options.Password),
            sources.GetPlan(name, Provider));
    }

    private string? Setting(string source, string key, string? fallback)
    {
        var value = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, source, key, fallback ?? string.Empty, this);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static OpenSearchOptions Validate(OpenSearchOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = OpenSearchRoute.NormalizeEndpoint(value.Endpoint);
        if (value.TimeoutSeconds <= 0) throw Invalid(nameof(value.TimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxRequestBytes <= 0) throw Invalid(nameof(value.MaxRequestBytes));
        if (value.MaxSearchCandidates <= 1 || value.MaxSearchCandidates > 10_000)
            throw new InvalidOperationException("OpenSearchOptions.MaxSearchCandidates must be between 2 and 10,000.");
        if (value.MaxResponseBytes <= 0) throw Invalid(nameof(value.MaxResponseBytes));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"OpenSearchOptions.{name} must be greater than zero.");
}
