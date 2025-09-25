using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.ElasticSearch;

internal sealed class ElasticSearchOptionsConfigurator(
    IConfiguration configuration,
    ILogger<ElasticSearchOptionsConfigurator> logger) : IConfigureOptions<ElasticSearchOptions>
{
    public void Configure(ElasticSearchOptions options)
    {
        logger.LogDebug("Configuring Elasticsearch options");

        options.IndexPrefix = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:IndexPrefix", options.IndexPrefix);
        options.IndexName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:IndexName", options.IndexName);
        options.VectorField = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:VectorField", options.VectorField) ?? options.VectorField;
        options.MetadataField = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:MetadataField", options.MetadataField) ?? options.MetadataField;
        options.IdField = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:IdField", options.IdField) ?? options.IdField;
        options.SimilarityMetric = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Similarity", options.SimilarityMetric) ?? options.SimilarityMetric;
        options.RefreshMode = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Refresh", options.RefreshMode) ?? options.RefreshMode;
        options.Dimension = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Dimension", options.Dimension);
        options.ApiKey = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:ApiKey", options.ApiKey);
        options.Username = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Username", options.Username);
        options.Password = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Password", options.Password);
        options.DisableIndexAutoCreate = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:DisableIndexAutoCreate", options.DisableIndexAutoCreate);

        options.Endpoint = ResolveEndpoint(options.Endpoint);
    }

    private string ResolveEndpoint(string? current)
    {
        var explicitEndpoint = Configuration.ReadFirst(configuration,
            $"{Infrastructure.Constants.Section}:Endpoint",
            $"{Infrastructure.Constants.Section}:BaseUrl",
            "ConnectionStrings:ElasticSearch",
            "ConnectionStrings:Elasticsearch");

        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            logger.LogDebug("Using explicit Elasticsearch endpoint {Endpoint}", explicitEndpoint);
            return explicitEndpoint!;
        }

        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, "auto", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Using preconfigured Elasticsearch endpoint {Endpoint}", current);
            return current!;
        }

        try
        {
            var discovery = new OrchestrationAwareServiceDiscovery(configuration);
            var result = discovery.DiscoverServiceAsync("elasticsearch", ServiceDiscoveryExtensions.ForHttpService("elasticsearch", 9200, "/_cluster/health")).GetAwaiter().GetResult();
            logger.LogInformation("Discovered Elasticsearch endpoint via {Method}: {Url}", result.DiscoveryMethod, result.ServiceUrl);
            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to localhost for Elasticsearch endpoint resolution");
            return "http://localhost:9200";
        }
    }
}
