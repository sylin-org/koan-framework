using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.AI.Connector.LMStudio.Infrastructure;

namespace Koan.AI.Connector.LMStudio.Options;

internal sealed class LMStudioOptionsConfigurator : AdapterOptionsConfigurator<LMStudioOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "LMStudio";

    public LMStudioOptionsConfigurator(
        IConfiguration config,
        ILogger<LMStudioOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    public LMStudioOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<LMStudioOptionsConfigurator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(LMStudioOptions options)
    {
    KoanLog.ConfigInfo(Logger, LogActions.Config, LocalLogOutcomes.Start);

        var explicitConnection = ReadProviderConfiguration(string.Empty,
            Constants.Configuration.Keys.ConnectionString,
            Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:LMStudio");

        var configuredBaseUrl = ReadProviderConfiguration(options.BaseUrl,
            "Koan:Ai:Provider:LMStudio:BaseUrl",
            "Koan:Ai:LMStudio:BaseUrl");

        var defaultModel = ReadProviderConfiguration(options.DefaultModel ?? string.Empty,
            "Koan:Ai:Provider:LMStudio:DefaultModel",
            "Koan:Ai:LMStudio:DefaultModel");

        var configuredApiKey = ReadProviderConfiguration(options.ApiKey ?? string.Empty,
            Constants.Configuration.Keys.ApiKey,
            Constants.Discovery.EnvKey);

        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            options.ConnectionString = explicitConnection;
            options.BaseUrl = explicitConnection;
            KoanLog.ConfigDebug(Logger, LogActions.Config, "connection-explicit",
                ("source", "configuration"));
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            options.ConnectionString = ResolveAutonomousConnection(defaultModel, options);
            options.BaseUrl = options.ConnectionString;
            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "auto", ("resolved", options.ConnectionString));
        }
        else
        {
            options.BaseUrl = options.ConnectionString;
            KoanLog.ConfigDebug(Logger, LogActions.Config, "connection-preconfigured",
                ("value", options.ConnectionString));
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            options.DefaultModel = defaultModel;
        }

        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            options.ApiKey = configuredApiKey;
        }

        options.AutoDiscoveryEnabled = ReadProviderConfiguration(options.AutoDiscoveryEnabled,
            "Koan:Ai:Provider:LMStudio:AutoDiscoveryEnabled",
            "Koan:Ai:LMStudio:AutoDiscoveryEnabled");

        if (int.TryParse(ReadProviderConfiguration(string.Empty, "Koan:Ai:Provider:LMStudio:Weight"), out var weight))
        {
            options.Weight = weight;
        }

        var labelsSection = Configuration.GetSection("Koan:Ai:Provider:LMStudio:Labels");
        if (labelsSection.Exists())
        {
            options.Labels = new Dictionary<string, string>();
            foreach (var entry in labelsSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    options.Labels[entry.Key] = entry.Value;
                }
            }
        }

    KoanLog.ConfigInfo(Logger, LogActions.Config, LocalLogOutcomes.Complete,
            ("connection", options.ConnectionString ?? "(null)"),
            ("defaultModel", options.DefaultModel ?? "(null)"),
            ("apiKey", string.IsNullOrWhiteSpace(options.ApiKey) ? "(none)" : "(set)"),
            ("autoDiscovery", options.AutoDiscoveryEnabled));
    }

    private string ResolveAutonomousConnection(string? defaultModel, LMStudioOptions options)
    {
        try
        {
            if (!options.AutoDiscoveryEnabled)
            {
                var fallback = options.BaseUrl;
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-disabled", ("fallback", fallback));
                return fallback;
            }

            if (_discoveryCoordinator == null)
            {
                var fallback = options.BaseUrl;
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, "coordinator-missing", ("fallback", fallback));
                return fallback;
            }

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(750),
                Parameters = new Dictionary<string, object>
                {
                    ["requiredModel"] = defaultModel ?? string.Empty,
                    ["apiKey"] = options.ApiKey ?? string.Empty
                }
            };

            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "request",
                ("mode", context.OrchestrationMode.ToString()),
                ("requiredModel", defaultModel ?? "(none)"));

            var result = _discoveryCoordinator.DiscoverServiceAsync(Constants.Adapter.Type, context)
                .GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "success",
                    ("url", result.ServiceUrl),
                    ("method", result.DiscoveryMethod));
                return result.ServiceUrl;
            }

            var fallbackFailure = options.BaseUrl;
            KoanLog.ConfigWarning(Logger, LogActions.Discovery, "failed",
                ("reason", result.ErrorMessage ?? "unknown"),
                ("fallback", fallbackFailure));
            return fallbackFailure;
        }
        catch (Exception ex)
        {
            var fallbackException = options.BaseUrl;
            KoanLog.ConfigWarning(Logger, LogActions.Discovery, "exception",
                ("reason", ex.Message),
                ("fallback", fallbackException));
            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "exception-detail",
                ("exception", ex.ToString()));
            return fallbackException;
        }
    }

    private static class LogActions
    {
        public const string Config = "lmstudio.config";
        public const string Discovery = "lmstudio.discovery";
    }

    private static class LocalLogOutcomes
    {
        public const string Start = "start";
        public const string Complete = "complete";
    }
}

