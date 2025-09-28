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

namespace Koan.Ai.Provider.Ollama.Options;

/// <summary>
/// Ollama configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class OllamaOptionsConfigurator : AdapterOptionsConfigurator<OllamaOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Ollama";

    public OllamaOptionsConfigurator(
        IConfiguration config,
        ILogger<OllamaOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public OllamaOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<OllamaOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(OllamaOptions options)
    {
        Logger?.LogInformation("Ollama Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', BaseUrl: '{BaseUrl}'",
            options.ConnectionString, options.BaseUrl);

        // Read Ollama-specific configuration
        var baseUrl = ReadProviderConfiguration(options.BaseUrl,
            "Koan:Ai:Provider:Ollama:BaseUrl",
            "Koan:Ai:Ollama:BaseUrl");

        var defaultModel = ReadProviderConfiguration(options.DefaultModel ?? "",
            "Koan:Ai:Provider:Ollama:DefaultModel",
            "Koan:Ai:Ollama:DefaultModel");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Ollama");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
            options.BaseUrl = explicitConnectionString; // For backward compatibility
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(defaultModel, options, Logger);
            options.BaseUrl = options.ConnectionString; // For backward compatibility
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
            options.BaseUrl = options.ConnectionString; // For backward compatibility
        }

        // Apply other configuration
        if (!string.IsNullOrWhiteSpace(defaultModel))
            options.DefaultModel = defaultModel;

        // Configure Ollama-specific options
        options.AutoDownloadModels = ReadProviderConfiguration(
            options.AutoDownloadModels,
            "Koan:Ai:Provider:Ollama:AutoDownloadModels",
            "Koan:Ai:Ollama:AutoDownloadModels");

        options.ModelDownloadTimeoutMinutes = ReadProviderConfiguration(
            options.ModelDownloadTimeoutMinutes,
            "Koan:Ai:Provider:Ollama:ModelDownloadTimeoutMinutes",
            "Koan:Ai:Ollama:ModelDownloadTimeoutMinutes");

        options.AutoDiscoveryEnabled = ReadProviderConfiguration(
            options.AutoDiscoveryEnabled,
            "Koan:Ai:Provider:Ollama:AutoDiscoveryEnabled",
            "Koan:Ai:Ollama:AutoDiscoveryEnabled");

        if (int.TryParse(ReadProviderConfiguration("", "Koan:Ai:Provider:Ollama:Weight"), out var weight))
            options.Weight = weight;

        // Parse labels if provided
        var labelsSection = Configuration.GetSection("Koan:Ai:Provider:Ollama:Labels");
        if (labelsSection.Exists())
        {
            options.Labels = new Dictionary<string, string>();
            foreach (var item in labelsSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                {
                    options.Labels[item.Key] = item.Value;
                }
            }
        }

        Logger?.LogInformation("Final Ollama Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("BaseUrl: {BaseUrl}", options.BaseUrl);
        Logger?.LogInformation("DefaultModel: {DefaultModel}", options.DefaultModel);
        Logger?.LogInformation("AutoDiscoveryEnabled: {AutoDiscoveryEnabled}", options.AutoDiscoveryEnabled);
        Logger?.LogInformation("Ollama Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(
        string? defaultModel,
        OllamaOptions options,
        ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
            }

            // Create discovery context with Ollama-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (!string.IsNullOrWhiteSpace(defaultModel))
                context.Parameters["requiredModel"] = defaultModel;

            context.Parameters["autoDownloadModels"] = options.AutoDownloadModels;

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("ollama", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Ollama discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous Ollama discovery failed, falling back to localhost");
                return $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Ollama discovery, falling back to localhost");
            return $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Ai:Provider:Ollama:DisableAutoDetection", false);
    }
}