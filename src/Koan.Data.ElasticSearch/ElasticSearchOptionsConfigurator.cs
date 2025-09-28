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

namespace Koan.Data.ElasticSearch;

/// <summary>
/// ElasticSearch configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class ElasticSearchOptionsConfigurator : AdapterOptionsConfigurator<ElasticSearchOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "ElasticSearch";

    public ElasticSearchOptionsConfigurator(
        IConfiguration config,
        ILogger<ElasticSearchOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public ElasticSearchOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<ElasticSearchOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(ElasticSearchOptions options)
    {
        Logger?.LogInformation("ElasticSearch Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Endpoint: '{Endpoint}'",
            options.ConnectionString, options.Endpoint);

        // Read ElasticSearch-specific configuration
        var endpoint = ReadProviderConfiguration(options.Endpoint,
            "Koan:Data:ElasticSearch:Endpoint",
            "Koan:Data:ElasticSearch:BaseUrl");

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            "Koan:Data:ElasticSearch:ApiKey");

        var username = ReadProviderConfiguration(options.Username ?? "",
            "Koan:Data:ElasticSearch:Username");

        var password = ReadProviderConfiguration(options.Password ?? "",
            "Koan:Data:ElasticSearch:Password");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:ElasticSearch",
            "ConnectionStrings:Elasticsearch");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
            options.Endpoint = explicitConnectionString; // For backward compatibility
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(Logger);
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }

        // Apply other configuration
        if (!string.IsNullOrWhiteSpace(apiKey))
            options.ApiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(username))
            options.Username = username;
        if (!string.IsNullOrWhiteSpace(password))
            options.Password = password;

        // Configure ElasticSearch-specific options
        options.IndexPrefix = ReadProviderConfiguration(
            options.IndexPrefix ?? "koan",
            "Koan:Data:ElasticSearch:IndexPrefix");
        options.IndexName = ReadProviderConfiguration(
            options.IndexName ?? "",
            "Koan:Data:ElasticSearch:IndexName");
        options.VectorField = ReadProviderConfiguration(
            options.VectorField,
            "Koan:Data:ElasticSearch:VectorField");
        options.MetadataField = ReadProviderConfiguration(
            options.MetadataField,
            "Koan:Data:ElasticSearch:MetadataField");
        options.IdField = ReadProviderConfiguration(
            options.IdField,
            "Koan:Data:ElasticSearch:IdField");
        options.SimilarityMetric = ReadProviderConfiguration(
            options.SimilarityMetric,
            "Koan:Data:ElasticSearch:SimilarityMetric");
        options.RefreshMode = ReadProviderConfiguration(
            options.RefreshMode,
            "Koan:Data:ElasticSearch:RefreshMode");
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            "Koan:Data:ElasticSearch:TimeoutSeconds");

        if (int.TryParse(ReadProviderConfiguration("", "Koan:Data:ElasticSearch:Dimension"), out var dimension))
            options.Dimension = dimension;

        options.DisableIndexAutoCreate = ReadProviderConfiguration(
            options.DisableIndexAutoCreate,
            "Koan:Data:ElasticSearch:DisableIndexAutoCreate");

        Logger?.LogInformation("Final ElasticSearch Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Endpoint: {Endpoint}", options.Endpoint);
        Logger?.LogInformation("ElasticSearch Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:9200";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return "http://localhost:9200";
            }

            // Create discovery context with ElasticSearch-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("elasticsearch", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("ElasticSearch discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous ElasticSearch discovery failed, falling back to localhost");
                return "http://localhost:9200";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous ElasticSearch discovery, falling back to localhost");
            return "http://localhost:9200";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:ElasticSearch:DisableAutoDetection", false);
    }
}
