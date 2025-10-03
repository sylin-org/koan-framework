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

namespace Koan.Data.Vector.Connector.Weaviate;

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
        ILogger<WeaviateOptionsConfigurator>? logger,
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
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Start);
        KoanLog.ConfigDebug(Logger, LogActions.Config, "context",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode));
        KoanLog.ConfigDebug(Logger, LogActions.Config, "initial-options",
            ("connection", options.ConnectionString ?? "(null)"),
            ("endpoint", options.Endpoint ?? "(null)"));

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
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-explicit", ("source", "configuration"));
            options.ConnectionString = explicitConnectionString;
            options.Endpoint = explicitConnectionString; // For backward compatibility
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-mode");
            options.ConnectionString = ResolveAutonomousConnection();
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }
        else
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-preconfigured");
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

        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomeValues.Final,
            ("connection", options.ConnectionString ?? "(null)"),
            ("endpoint", options.Endpoint ?? "(null)"),
            ("metric", options.Metric ?? "(null)"));
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Complete);
    }

    private string ResolveAutonomousConnection()
    {
        const string fallback = "http://localhost:8080";
        try
        {
            if (IsAutoDetectionDisabled())
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-disabled", ("fallback", fallback));
                return fallback;
            }

            if (_discoveryCoordinator == null)
            {
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, "coordinator-missing", ("fallback", fallback));
                return fallback;
            }

            // Create discovery context with Weaviate-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            KoanLog.ConfigDebug(Logger, LogActions.DiscoveryRequest, LogOutcomes.Start,
                ("mode", context.OrchestrationMode.ToString()));

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("weaviate", context);
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
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, LogOutcomeValues.Failed,
                    ("reason", result.ErrorMessage ?? "unknown"),
                    ("fallback", fallback));
                return fallback;
            }
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(Logger, LogActions.Discovery, "exception",
                ("reason", ex.Message),
                ("fallback", fallback));
            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "exception-detail", ("exception", ex.ToString()));
            return fallback;
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Weaviate:DisableAutoDetection", false);
    }

    private static class LogActions
    {
        public const string Config = "weaviate.config";
        public const string Discovery = "weaviate.discovery";
        public const string DiscoveryRequest = "weaviate.discovery.request";
    }

    private static class LogOutcomeValues
    {
        public const string Final = "final";
        public const string Success = "success";
        public const string Failed = "failed";
    }
}
