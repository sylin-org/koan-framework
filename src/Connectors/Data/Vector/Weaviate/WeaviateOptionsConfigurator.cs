using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Core.Infrastructure;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.ZenGarden;
using WeaviateItems = Koan.Data.Vector.Connector.Weaviate.Infrastructure.WeaviateProvenanceItems;

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
        var endpoint = ReadProviderConfiguration(options.Endpoint, WeaviateItems.EndpointKeys);

        // User-explicit endpoint always beats auto-discovery.
        // If the user configured an endpoint that differs from the default, it is authoritative.
        var hasUserExplicitEndpoint = !string.IsNullOrWhiteSpace(endpoint)
            && !string.Equals(endpoint, new WeaviateOptions().Endpoint, StringComparison.OrdinalIgnoreCase);

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "", WeaviateItems.ApiKeyKeys);

        var explicitConnectionString = ReadProviderConfiguration("", WeaviateItems.ConnectionStringKeys);

        var requestedConnection = !string.IsNullOrWhiteSpace(explicitConnectionString)
            ? explicitConnectionString
            : options.ConnectionString;

        if (ZenGardenConnectionIntent.TryParse(requestedConnection, out var zenGardenIntent))
        {
            var resolved = ResolveRequiredConnection(requestedConnection!, zenGardenIntent!);
            options.ConnectionString = resolved;
            if (!hasUserExplicitEndpoint) options.Endpoint = resolved;
            KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "intent-resolved",
                ("offering", zenGardenIntent!.ToOfferingSelector()));
        }
        else if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-explicit", ("source", "configuration"));
            options.ConnectionString = explicitConnectionString;
            if (!hasUserExplicitEndpoint)
                options.Endpoint = explicitConnectionString;
        }
        else if (IsAutoConnection(requestedConnection))
        {
            // Discovery mode: always run the health-checked candidate probe. Zen Garden (if referenced)
            // contributes its resolved offering endpoint into that probe as ONE health-checked candidate rather
            // than short-circuiting here, so an unreachable ZG answer falls through to the standard candidates.
            KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-mode");
            options.ConnectionString = ResolveAutonomousConnection();
            if (!hasUserExplicitEndpoint)
                options.Endpoint = options.ConnectionString;
        }
        else
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-preconfigured");
            options.ConnectionString = requestedConnection ?? "";
            if (!hasUserExplicitEndpoint)
                options.Endpoint = options.ConnectionString;
        }

        // Apply user-explicit endpoint — final authority, never overridden by discovery
        if (hasUserExplicitEndpoint)
        {
            options.Endpoint = endpoint!;
            KoanLog.ConfigInfo(Logger, LogActions.Config, "endpoint-user-explicit",
                ("endpoint", endpoint!),
                ("note", "User configuration beats auto-discovery"));
        }

        // Apply other configuration
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            options.ApiKey = apiKey;
        }

        // Configure Weaviate-specific options
        options.DefaultTopK = ReadProviderConfiguration(options.DefaultTopK, WeaviateItems.DefaultTopKKeys);
        options.MaxTopK = ReadProviderConfiguration(options.MaxTopK, WeaviateItems.MaxTopKKeys);
        options.Dimension = ReadProviderConfiguration(options.Dimension, WeaviateItems.DimensionKeys);
        options.Metric = ReadProviderConfiguration(options.Metric, WeaviateItems.MetricKeys);
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(options.DefaultTimeoutSeconds, WeaviateItems.TimeoutKeys);

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
            var discoveryTask = _discoveryCoordinator.DiscoverService("weaviate", context);
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
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Flags.DisableAutoDetection, false);
    }

    private string ResolveRequiredConnection(
        string rawIntent,
        ZenGardenConnectionIntent intent)
    {
        if (_discoveryCoordinator is null)
        {
            throw ExplicitIntentFailure(
                intent,
                "Koan's service-discovery coordinator is unavailable.");
        }

        var context = new DiscoveryContext
        {
            OrchestrationMode = KoanEnv.OrchestrationMode,
            HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
            Parameters = new Dictionary<string, object>()
        };
        var result = _discoveryCoordinator.ResolveServiceIntent("weaviate", rawIntent, context)
            .GetAwaiter()
            .GetResult();
        if (!result.IsSuccessful)
        {
            throw ExplicitIntentFailure(intent, result.ErrorMessage);
        }

        return result.ServiceUrl;
    }

    private static bool IsAutoConnection(string? connectionString)
    {
        return string.IsNullOrWhiteSpace(connectionString)
            || string.Equals(connectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException ExplicitIntentFailure(
        ZenGardenConnectionIntent intent,
        string? reason) =>
        new(
            $"Weaviate explicit Zen Garden intent for '{intent.ToOfferingSelector()}' could not be satisfied. " +
            $"{reason ?? "No ready Weaviate offering was found."} " +
            "Reference and enable Koan.ZenGarden with a ready 'weaviate' offering, choose 'auto', or provide a native Weaviate endpoint.");

    private static class LogActions
    {
        public const string Config = "weaviate.config";
        public const string Discovery = "weaviate.discovery";
        public const string DiscoveryRequest = "weaviate.discovery.request";
        public const string ZenGarden = "weaviate.zengarden";
    }

    private static class LogOutcomeValues
    {
        public const string Final = "final";
        public const string Success = "success";
        public const string Failed = "failed";
    }
}
