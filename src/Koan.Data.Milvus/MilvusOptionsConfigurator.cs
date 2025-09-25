
using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Milvus;

internal sealed class MilvusOptionsConfigurator(
    IConfiguration configuration,
    ILogger<MilvusOptionsConfigurator> logger) : IConfigureOptions<MilvusOptions>
{
    public void Configure(MilvusOptions options)
    {
        logger.LogDebug("Configuring Milvus options");

        options.DatabaseName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Database", options.DatabaseName) ?? options.DatabaseName;
        options.CollectionName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Collection", options.CollectionName);
        options.PrimaryFieldName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:PrimaryField", options.PrimaryFieldName) ?? options.PrimaryFieldName;
        options.VectorFieldName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:VectorField", options.VectorFieldName) ?? options.VectorFieldName;
        options.MetadataFieldName = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:MetadataField", options.MetadataFieldName) ?? options.MetadataFieldName;
        options.Metric = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Metric", options.Metric) ?? options.Metric;
        options.Dimension = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Dimension", options.Dimension);
        options.AutoCreateCollection = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:AutoCreate", options.AutoCreateCollection);
        options.ConsistencyLevel = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Consistency", options.ConsistencyLevel) ?? options.ConsistencyLevel;
        options.Token = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Token", options.Token);
        options.Username = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Username", options.Username);
        options.Password = Configuration.Read(configuration, $"{Infrastructure.Constants.Section}:Password", options.Password);

        options.Endpoint = ResolveEndpoint(options.Endpoint);
    }

    private string ResolveEndpoint(string? current)
    {
        var explicitEndpoint = Configuration.ReadFirst(configuration,
            $"{Infrastructure.Constants.Section}:Endpoint",
            "ConnectionStrings:Milvus",
            "ConnectionStrings:milvus");

        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            logger.LogDebug("Using explicit Milvus endpoint {Endpoint}", explicitEndpoint);
            return explicitEndpoint!;
        }

        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, "auto", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Using preconfigured Milvus endpoint {Endpoint}", current);
            return current!;
        }

        try
        {
            var discovery = new OrchestrationAwareServiceDiscovery(configuration);
            var result = discovery.DiscoverServiceAsync("milvus", ServiceDiscoveryExtensions.ForHttpService("milvus", 19530, "/v2/health")).GetAwaiter().GetResult();
            logger.LogInformation("Discovered Milvus endpoint via {Method}: {Url}", result.DiscoveryMethod, result.ServiceUrl);
            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to localhost for Milvus endpoint resolution");
            return "http://localhost:19530";
        }
    }
}
