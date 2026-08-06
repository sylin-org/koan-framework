using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Milvus;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "Milvus",
    ContainerImage = "milvusdb/milvus", DefaultTag = "v2.6.20",
    DefaultPorts = new[] { 19530 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true", "session-visibility=true" },
    Env = new[] { "ETCD_ENDPOINTS=etcd:2379", "MINIO_ADDRESS=minio:9000" },
    Volumes = new[] { "./Data/milvus-2.6:/var/lib/milvus" },
    AppEnv = new[] { "Koan__Data__Milvus__Endpoint=http://{serviceId}:{port}" },
    Scheme = "http", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 19530,
    UriPattern = "http://{host}:{port}", LocalScheme = "http", LocalHost = "localhost",
    LocalPort = 19530, LocalPattern = "http://{host}:{port}")]
public sealed class MilvusVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<MilvusOptions> options) : IVectorAdapterFactory
{
    private readonly MilvusOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.FullNamespace,
        Separator = "_",
        Casing = NameCasing.AsIs,
        PartitionSeparator = '_',
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = false, AllowedExtraChars = "_" }
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
                "Milvus realizes awaited Session visibility with Strong reads and does not simulate Eventual visibility.");
        var http = services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory
            ?? throw new InvalidOperationException("Milvus requires IHttpClientFactory. Reference the connector and call AddKoan().");
        return new MilvusRepository<TEntity, TKey>(services, this, plan, ResolveRoute(plan.Source), _options, http);
    }

    internal MilvusRoute ResolveRoute(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var endpoint = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, sources, Provider, name, _options.Endpoint, this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "Database", _options.Database, this);
        var token = AdapterConnectionResolver.GetSourceSetting(
            configuration, sources, Provider, name, "Token", _options.Token ?? string.Empty, this);
        return MilvusRoute.Create(name, endpoint, database,
            string.IsNullOrWhiteSpace(token) ? null : token, sources.GetPlan(name, Provider));
    }

    private static MilvusOptions Validate(MilvusOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = MilvusRoute.NormalizeEndpoint(value.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Database);
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
        new($"MilvusOptions.{name} must be greater than zero.");
}
