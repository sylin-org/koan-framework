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
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.AI.Connector.Ollama.Options;

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
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Start);
        KoanLog.ConfigDebug(Logger, LogActions.Config, "context",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode));
        KoanLog.ConfigDebug(Logger, LogActions.Config, "initial-options",
            ("connection", options.ConnectionString ?? "(null)"),
            ("baseUrl", options.BaseUrl ?? "(null)"));

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
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-explicit", ("source", "configuration"));
            options.ConnectionString = explicitConnectionString;
            options.BaseUrl = explicitConnectionString; // For backward compatibility
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-mode", ("defaultModel", defaultModel ?? "(none)"));
            options.ConnectionString = ResolveAutonomousConnection(defaultModel, options);
            options.BaseUrl = options.ConnectionString; // For backward compatibility
        }
        else
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-preconfigured");
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

        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomeValues.Final,
            ("connection", options.ConnectionString ?? "(null)"),
            ("baseUrl", options.BaseUrl ?? "(null)"),
            ("defaultModel", options.DefaultModel ?? "(null)"),
            ("autoDiscovery", options.AutoDiscoveryEnabled));
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Complete);
    }

    private string ResolveAutonomousConnection(
        string? defaultModel,
        OllamaOptions options)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                var fallbackUrl = $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-disabled", ("fallback", fallbackUrl));
                return fallbackUrl;
            }

            if (_discoveryCoordinator == null)
            {
                var fallbackUrl = $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, "coordinator-missing", ("fallback", fallbackUrl));
                return fallbackUrl;
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

            var requiredModelParam = "(none)";
            if (context.Parameters is { } parameters && parameters.TryGetValue("requiredModel", out var requiredModelValue))
            {
                requiredModelParam = requiredModelValue?.ToString() ?? "(null)";
            }

            KoanLog.ConfigDebug(Logger, LogActions.DiscoveryRequest, LogOutcomes.Start,
                ("mode", context.OrchestrationMode.ToString()),
                ("requiredModel", requiredModelParam),
                ("autoDownload", options.AutoDownloadModels));

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("ollama", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, LogOutcomeValues.Success,
                    ("url", result.ServiceUrl),
                    ("method", result.DiscoveryMethod),
                    ("healthy", result.IsHealthy));
                return result.ServiceUrl;
            }
            else
            {
                var fallbackUrl = $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, LogOutcomeValues.Failed,
                    ("reason", result.ErrorMessage ?? "unknown"),
                    ("fallback", fallbackUrl));
                return fallbackUrl;
            }
        }
        catch (Exception ex)
        {
            var fallbackUrl = $"http://localhost:{Infrastructure.Constants.Discovery.DefaultPort}";
            KoanLog.ConfigWarning(Logger, LogActions.Discovery, "exception",
                ("reason", ex.Message),
                ("fallback", fallbackUrl));
            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "exception-detail", ("exception", ex.ToString()));
            return fallbackUrl;
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Ai:Provider:Ollama:DisableAutoDetection", false);
    }

    private static class LogActions
    {
        public const string Config = "ollama.config";
        public const string Discovery = "ollama.discovery";
        public const string DiscoveryRequest = "ollama.discovery.request";
    }

    private static class LogOutcomeValues
    {
        public const string Final = "final";
        public const string Success = "success";
        public const string Failed = "failed";
    }
}
