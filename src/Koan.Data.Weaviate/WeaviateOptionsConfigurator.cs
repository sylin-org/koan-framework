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

namespace Koan.Data.Weaviate;

/// <summary>
/// Weaviate configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class WeaviateOptionsConfigurator : AdapterOptionsConfigurator<WeaviateOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Weaviate";

    public WeaviateOptionsConfigurator(
        IConfiguration config,
        ILogger<WeaviateOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public WeaviateOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<WeaviateOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(WeaviateOptions options)
    {
        Logger?.LogInformation("Weaviate Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Endpoint: '{Endpoint}'",
            options.ConnectionString, options.Endpoint);

        // Read Weaviate-specific configuration
        var endpoint = ReadProviderConfiguration(options.Endpoint,
            "Koan:Data:Weaviate:Endpoint",
            "Koan:Data:Weaviate:BaseUrl");

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            "Koan:Data:Weaviate:ApiKey",
            "Koan:Data:Weaviate:Key");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Weaviate",
            "ConnectionStrings:weaviate");

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
        {
            options.ApiKey = apiKey;
        }

        // Configure Weaviate-specific options
        options.DefaultTopK = ReadProviderConfiguration(
            options.DefaultTopK,
            "Koan:Data:Weaviate:DefaultTopK");
        options.MaxTopK = ReadProviderConfiguration(
            options.MaxTopK,
            "Koan:Data:Weaviate:MaxTopK");
        options.Dimension = ReadProviderConfiguration(
            options.Dimension,
            "Koan:Data:Weaviate:Dimension");
        options.Metric = ReadProviderConfiguration(
            options.Metric,
            "Koan:Data:Weaviate:Metric");
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            "Koan:Data:Weaviate:TimeoutSeconds");

        Logger?.LogInformation("Final Weaviate Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Endpoint: {Endpoint}", options.Endpoint);
        Logger?.LogInformation("Weaviate Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:8080";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return "http://localhost:8080";
            }

            // Create discovery context with Weaviate-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("weaviate", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Weaviate discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous Weaviate discovery failed, falling back to localhost");
                return "http://localhost:8080";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Weaviate discovery, falling back to localhost");
            return "http://localhost:8080";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Weaviate:DisableAutoDetection", false);
    }
}