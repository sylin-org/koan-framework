using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using Koan.ZenGarden.Core;
using WeaviateItems = Koan.Data.Vector.Connector.Weaviate.Infrastructure.WeaviateProvenanceItems;

namespace Koan.Data.Vector.Connector.Weaviate;

/// <summary>
/// Weaviate configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class WeaviateOptionsConfigurator : AdapterOptionsConfigurator<WeaviateOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;
    private readonly IZenGardenInitializationProvider? _zenGardenInitializationProvider;

    protected override string ProviderName => "Weaviate";

    public WeaviateOptionsConfigurator(
        IConfiguration config,
        ILogger<WeaviateOptionsConfigurator>? logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null,
        IZenGardenInitializationProvider? zenGardenInitializationProvider = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
        _zenGardenInitializationProvider = zenGardenInitializationProvider;
    }

    // Simplified constructor for orchestration scenarios without DI
    public WeaviateOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<WeaviateOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
        _zenGardenInitializationProvider = null;
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
            if (TryResolveZenGardenConnection(zenGardenIntent!, out var resolved))
            {
                options.ConnectionString = resolved;
                if (!hasUserExplicitEndpoint)
                    options.Endpoint = resolved;
                KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "intent-resolved", ("intent", requestedConnection));
            }
            else
            {
                options.ConnectionString = ResolveAutonomousConnection();
                if (!hasUserExplicitEndpoint)
                    options.Endpoint = options.ConnectionString;
                KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "intent-fallback-autonomous", ("intent", requestedConnection));
            }
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
            var defaultIntent = BuildDefaultZenGardenIntent();
            if (TryResolveZenGardenConnection(defaultIntent, out var resolved))
            {
                options.ConnectionString = resolved;
                if (!hasUserExplicitEndpoint)
                    options.Endpoint = resolved;
                KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "auto-resolved", ("offering", defaultIntent.ToOfferingSelector()));
            }
            else
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-mode");
                options.ConnectionString = ResolveAutonomousConnection();
                if (!hasUserExplicitEndpoint)
                    options.Endpoint = options.ConnectionString;
            }
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

    private ZenGardenConnectionIntent BuildDefaultZenGardenIntent()
    {
        var configuredOffering = ReadProviderConfiguration(
            "",
            "Koan:Data:Weaviate:ZenGarden:Offering");

        if (string.IsNullOrWhiteSpace(configuredOffering) &&
            _zenGardenInitializationProvider?.TryGetDefaultOffering("weaviate", out var mappedOffering) == true)
        {
            configuredOffering = mappedOffering;
        }

        if (string.IsNullOrWhiteSpace(configuredOffering))
        {
            configuredOffering = "weaviate";
        }

        var configuredInstance = ReadProviderConfiguration(
            "",
            "Koan:Data:Weaviate:ZenGarden:Instance");

        return ZenGardenConnectionIntent.ForOffering(
            configuredOffering,
            configuredInstance,
            ReadZenGardenCapabilities());
    }

    private IReadOnlyList<string> ReadZenGardenCapabilities()
    {
        var sectionValues = Configuration
            .GetSection("Koan:Data:Weaviate:ZenGarden:Capabilities")
            .Get<string[]>() ?? [];

        var singleValue = ReadProviderConfiguration(
            "",
            "Koan:Data:Weaviate:ZenGarden:Capability");

        var parsed = new List<string>();
        foreach (var raw in sectionValues)
        {
            AppendCapabilities(raw, parsed);
        }

        AppendCapabilities(singleValue, parsed);

        return new ReadOnlyCollection<string>(parsed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToLowerInvariant())
            .ToArray());
    }

    private static void AppendCapabilities(string? raw, ICollection<string> output)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var token in raw.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                output.Add(token.Trim());
            }
        }
    }

    private bool TryResolveZenGardenConnection(
        ZenGardenConnectionIntent intent,
        out string connectionString)
    {
        connectionString = "";
        if (_zenGardenInitializationProvider is null)
        {
            KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "provider-missing");
            return false;
        }

        try
        {
            var resolved = _zenGardenInitializationProvider
                .Resolve(intent)
                .GetAwaiter()
                .GetResult();

            if (resolved is null)
            {
                KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "offering-not-ready",
                    ("offering", intent.ToOfferingSelector()));
                return false;
            }

            var directUri = resolved.GetUri("http", "https");
            if (!string.IsNullOrWhiteSpace(directUri) &&
                Uri.TryCreate(directUri, UriKind.Absolute, out var parsedUri))
            {
                connectionString = NormalizeEndpoint(parsedUri);
                KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "resolved",
                    ("offering", resolved.ToolFqid),
                    ("connection", connectionString));
                return true;
            }

            var host = !string.IsNullOrWhiteSpace(resolved.Hostname)
                ? resolved.Hostname
                : resolved.Ip;
            if (string.IsNullOrWhiteSpace(host))
            {
                KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "missing-endpoint",
                    ("offering", resolved.ToolFqid));
                return false;
            }

            var port = resolved.Port ?? 8080;
            connectionString = $"http://{host}:{port}";
            KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "resolved",
                ("offering", resolved.ToolFqid),
                ("connection", connectionString));
            return true;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "exception",
                ("offering", intent.ToOfferingSelector()),
                ("reason", ex.Message));
            KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "exception-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private static bool IsAutoConnection(string? connectionString)
    {
        return string.IsNullOrWhiteSpace(connectionString)
            || string.Equals(connectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEndpoint(Uri uri)
    {
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}";
    }

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
