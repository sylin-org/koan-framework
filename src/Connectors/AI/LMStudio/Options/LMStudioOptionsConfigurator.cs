using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.AI.Connector.LMStudio.Infrastructure;

namespace Koan.AI.Connector.LMStudio.Options;

internal sealed class LMStudioOptionsConfigurator : IConfigureOptions<LMStudioOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public LMStudioOptionsConfigurator(
        IConfiguration config,
        ILogger<LMStudioOptionsConfigurator> logger,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
    {
        _configuration = config;
        _logger = logger;
        _discoveryCoordinator = discoveryCoordinator;
    }

    public LMStudioOptionsConfigurator(IConfiguration config)
    {
        _configuration = config;
        _logger = NullLogger<LMStudioOptionsConfigurator>.Instance;
        _discoveryCoordinator = null;
    }

    public void Configure(LMStudioOptions options)
    {
        KoanLog.ConfigInfo(_logger, LogActions.Config, LocalLogOutcomes.Start);

        var explicitConnection = ReadProviderConfiguration("",
            Constants.Configuration.Keys.ConnectionString,
            Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:LMStudio");

        var configuredBaseUrl = ReadProviderConfiguration(options.BaseUrl,
            Constants.Configuration.Keys.BaseUrl,
            Constants.Configuration.Keys.AltBaseUrl);

        var defaultModel = ReadProviderConfiguration(options.DefaultModel ?? "",
            Constants.Configuration.Keys.DefaultModel,
            Constants.Configuration.Keys.AltDefaultModel);

        var configuredApiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            Constants.Configuration.Keys.ApiKey,
            Constants.Discovery.EnvKey);

        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            options.ConnectionString = explicitConnection;
            options.BaseUrl = explicitConnection;
            KoanLog.ConfigDebug(_logger, LogActions.Config, "connection-explicit",
                ("source", "configuration"));
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            options.ConnectionString = ResolveAutonomousConnection(defaultModel, options);
            options.BaseUrl = options.ConnectionString;
            KoanLog.ConfigDebug(_logger, LogActions.Discovery, "auto", ("resolved", options.ConnectionString));
        }
        else
        {
            options.BaseUrl = options.ConnectionString;
            KoanLog.ConfigDebug(_logger, LogActions.Config, "connection-preconfigured",
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
            Constants.Configuration.Keys.AutoDiscoveryEnabled,
            Constants.Configuration.Keys.AltAutoDiscoveryEnabled);

        if (int.TryParse(ReadProviderConfiguration("", Constants.Configuration.Keys.Weight), out var weight))
        {
            options.Weight = weight;
        }

        var labelsSection = _configuration.GetSection(Constants.Configuration.Keys.Labels);
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

        KoanLog.ConfigInfo(_logger, LogActions.Config, LocalLogOutcomes.Complete,
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
                KoanLog.ConfigInfo(_logger, LogActions.Discovery, "auto-disabled", ("fallback", fallback));
                return fallback;
            }

            if (_discoveryCoordinator == null)
            {
                var fallback = options.BaseUrl;
                KoanLog.ConfigWarning(_logger, LogActions.Discovery, "coordinator-missing", ("fallback", fallback));
                return fallback;
            }

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(750),
                Parameters = new Dictionary<string, object>
                {
                    ["requiredModel"] = defaultModel ?? "",
                    ["apiKey"] = options.ApiKey ?? ""
                }
            };

            KoanLog.ConfigDebug(_logger, LogActions.Discovery, "request",
                ("mode", context.OrchestrationMode.ToString()),
                ("requiredModel", defaultModel ?? "(none)"));

            var result = _discoveryCoordinator.DiscoverService(Constants.Adapter.Type, context)
                .GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                KoanLog.ConfigInfo(_logger, LogActions.Discovery, "success",
                    ("url", result.ServiceUrl),
                    ("method", result.DiscoveryMethod));
                return result.ServiceUrl;
            }

            var fallbackFailure = options.BaseUrl;
            KoanLog.ConfigWarning(_logger, LogActions.Discovery, "failed",
                ("reason", result.ErrorMessage ?? "unknown"),
                ("fallback", fallbackFailure));
            return fallbackFailure;
        }
        catch (Exception ex)
        {
            var fallbackException = options.BaseUrl;
            KoanLog.ConfigWarning(_logger, LogActions.Discovery, "exception",
                ("reason", ex.Message),
                ("fallback", fallbackException));
            KoanLog.ConfigDebug(_logger, LogActions.Discovery, "exception-detail",
                ("exception", ex.ToString()));
            return fallbackException;
        }
    }

    private T ReadProviderConfiguration<T>(T defaultValue, params string[] keys)
    {
        if (typeof(T) == typeof(string))
        {
            var result = Core.Configuration.ReadFirst(_configuration, keys) ?? defaultValue?.ToString() ?? "";
            return (T)(object)result;
        }

        if (typeof(T) == typeof(bool))
        {
            return (T)(object)Core.Configuration.Read(_configuration, keys[0], (bool)(object)defaultValue!);
        }

        var configured = Core.Configuration.ReadFirst(_configuration, defaultValue?.ToString() ?? "", keys);
        try
        {
            return (T)Convert.ChangeType(configured, typeof(T));
        }
        catch
        {
            return defaultValue;
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

