using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.SearchEngine;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.OpenSearch;

[ProviderPriority(20)]
[KoanService(ServiceKind.Vector, shortCode: "opensearch", name: "OpenSearch",
    ContainerImage = "opensearchproject/opensearch",
    DefaultTag = "2.13.0",
    DefaultPorts = new[] { 9200 },
    Capabilities = new[] { "protocol=http", "vector-search=true", "filters=true" },
    Env = new[]
    {
        "discovery.type=single-node",
        "DISABLE_SECURITY_PLUGIN=true",
        "OPENSEARCH_JAVA_OPTS=-Xms512m -Xmx512m"
    },
    Volumes = new[] { "./Data/opensearch:/usr/share/opensearch/data" },
    AppEnv = new[] { "Koan__Data__OpenSearch__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/_cluster/health",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "opensearch", EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
public sealed class OpenSearchVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "opensearch";

    // ARCH-0103 §4.1: accepts the routed source for contract alignment; per-source physical placement (per-index /
    // per-cluster) is realized in P4.
    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<OpenSearchOptions>?)sp.GetService(typeof(IOptions<OpenSearchOptions>))
            ?? throw new InvalidOperationException("OpenSearchOptions not configured; bind Koan:Data:OpenSearch.");
        var logger = ((ILoggerFactory?)sp.GetService(typeof(ILoggerFactory)))
            ?.CreateLogger<SearchEngineVectorRepository<TEntity, TKey>>();
        return new SearchEngineVectorRepository<TEntity, TKey>(
            httpFactory.CreateClient(Infrastructure.Constants.HttpClientName),
            options.Value,
            new OpenSearchDialect(),
            OpenSearchTelemetry.Activity,
            logger,
            sp);
    }

    // OpenSearch index names are lowercase; the partition uses '-'. (Names are EntityType, so the name
    // separator is irrelevant; the optional IndexPrefix is applied by the repository.)
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.Lower,
            PartitionSeparator = '-',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true, AllowedExtraChars = "-._" },
        };
}

