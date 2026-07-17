using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Core.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.ElasticSearch;

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
        LogConfiguration(LogLevel.Debug, "initial",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode),
            ("connection", options.ConnectionString),
            ("endpoint", options.Endpoint));

        // Read ElasticSearch-specific configuration
        var endpoint = ReadProviderConfiguration(options.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            Infrastructure.Constants.Configuration.Keys.BaseUrl);

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            Infrastructure.Constants.Configuration.Keys.ApiKey);

        var username = ReadProviderConfiguration(options.Username ?? "",
            Infrastructure.Constants.Configuration.Keys.Username);

        var password = ReadProviderConfiguration(options.Password ?? "",
            Infrastructure.Constants.Configuration.Keys.Password);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:ElasticSearch",
            "ConnectionStrings:Elasticsearch");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            LogConfiguration(LogLevel.Information, "explicit");
            options.ConnectionString = explicitConnectionString;
            options.Endpoint = explicitConnectionString; // For backward compatibility
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            LogConfiguration(LogLevel.Information, "auto");
            options.ConnectionString = ResolveAutonomousConnection();
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }
        else
        {
            LogConfiguration(LogLevel.Information, "preconfigured");
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
            Infrastructure.Constants.Configuration.Keys.IndexPrefix);
        options.IndexName = ReadProviderConfiguration(
            options.IndexName ?? "",
            Infrastructure.Constants.Configuration.Keys.IndexName);
        options.VectorField = ReadProviderConfiguration(
            options.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorField);
        options.MetadataField = ReadProviderConfiguration(
            options.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataField);
        options.IdField = ReadProviderConfiguration(
            options.IdField,
            Infrastructure.Constants.Configuration.Keys.IdField);
        options.SimilarityMetric = ReadProviderConfiguration(
            options.SimilarityMetric,
            Infrastructure.Constants.Configuration.Keys.SimilarityMetric);
        options.RefreshMode = ReadProviderConfiguration(
            options.RefreshMode,
            Infrastructure.Constants.Configuration.Keys.RefreshMode);
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds);

        if (int.TryParse(ReadProviderConfiguration("", Infrastructure.Constants.Configuration.Keys.Dimension), out var dimension))
            options.Dimension = dimension;

        options.DisableIndexAutoCreate = ReadProviderConfiguration(
            options.DisableIndexAutoCreate,
            Infrastructure.Constants.Configuration.Keys.DisableIndexAutoCreate);

        LogConfiguration(LogLevel.Information, "final",
            ("connection", options.ConnectionString),
            ("endpoint", options.Endpoint));
    }

    private string ResolveAutonomousConnection()
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                LogDiscovery(LogLevel.Information, "disabled", ("fallback", "http://localhost:9200"));
                return "http://localhost:9200";
            }

            if (_discoveryCoordinator == null)
            {
                LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", "http://localhost:9200"));
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
            var discoveryTask = _discoveryCoordinator.DiscoverService("elasticsearch", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }
            else
            {
                LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", "http://localhost:9200"));
                return "http://localhost:9200";
            }
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", "http://localhost:9200"));
            return "http://localhost:9200";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }
}

