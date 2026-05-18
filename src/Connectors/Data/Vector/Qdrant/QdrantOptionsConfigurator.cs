using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Qdrant configuration using autonomous service discovery. Pattern matches the Milvus / ES /
/// OS adapters: explicit connection string wins, else "auto" triggers the discovery coordinator,
/// else literal endpoint is honored verbatim.
/// </summary>
internal sealed class QdrantOptionsConfigurator : AdapterOptionsConfigurator<QdrantOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Qdrant";

    public QdrantOptionsConfigurator(
        IConfiguration config,
        ILogger<QdrantOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    public QdrantOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<QdrantOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(QdrantOptions options)
    {
        Logger?.LogInformation("Qdrant Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Endpoint: '{Endpoint}'",
            options.ConnectionString, options.Endpoint);

        var endpoint = ReadProviderConfiguration(options.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint);

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            Infrastructure.Constants.Configuration.Keys.ApiKey);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Qdrant",
            "ConnectionStrings:qdrant");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
            options.Endpoint = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(apiKey, Logger);
            options.Endpoint = options.ConnectionString;
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
            options.Endpoint = options.ConnectionString;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
            options.ApiKey = apiKey;

        options.CollectionName = ReadProviderConfiguration(
            options.CollectionName ?? "",
            Infrastructure.Constants.Configuration.Keys.Collection,
            Infrastructure.Constants.Configuration.Keys.CollectionName);

        options.Distance = ReadProviderConfiguration(
            options.Distance,
            Infrastructure.Constants.Configuration.Keys.Distance,
            Infrastructure.Constants.Configuration.Keys.Metric);

        options.IdField = ReadProviderConfiguration(
            options.IdField,
            Infrastructure.Constants.Configuration.Keys.IdField);

        options.VectorField = ReadProviderConfiguration(
            options.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorFieldName);

        options.MetadataField = ReadProviderConfiguration(
            options.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataFieldName);

        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds);

        if (int.TryParse(ReadProviderConfiguration("", Infrastructure.Constants.Configuration.Keys.Dimension), out var dimension))
            options.Dimension = dimension;

        options.AutoCreateCollection = ReadProviderConfiguration(
            options.AutoCreateCollection,
            Infrastructure.Constants.Configuration.Keys.AutoCreate,
            Infrastructure.Constants.Configuration.Keys.AutoCreateCollection);

        options.WaitForResult = ReadProviderConfiguration(
            options.WaitForResult,
            Infrastructure.Constants.Configuration.Keys.WaitForResult);

        options.OnDisk = ReadProviderConfiguration(
            options.OnDisk,
            Infrastructure.Constants.Configuration.Keys.OnDisk);

        Logger?.LogInformation("Final Qdrant Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Endpoint: {Endpoint}", options.Endpoint);
        Logger?.LogInformation("Distance: {Distance}, Dimension: {Dimension}, WaitForResult: {Wait}",
            options.Distance, options.Dimension, options.WaitForResult);
        Logger?.LogInformation("Qdrant Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(string? apiKey, ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:6333";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return "http://localhost:6333";
            }

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                context.Parameters["apiKey"] = apiKey;

            var discoveryTask = _discoveryCoordinator.DiscoverService("qdrant", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Qdrant discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }

            logger?.LogWarning("Autonomous Qdrant discovery failed, falling back to localhost");
            return "http://localhost:6333";
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Qdrant discovery, falling back to localhost");
            return "http://localhost:6333";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }
}
