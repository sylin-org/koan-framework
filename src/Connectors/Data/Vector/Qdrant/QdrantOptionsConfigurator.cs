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

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Qdrant configuration using autonomous service discovery. Pattern matches the Milvus / ES /
/// OS adapters: explicit connection string wins, else "auto" triggers the discovery coordinator,
/// else literal endpoint is honored verbatim.
/// </summary>
internal sealed class QdrantOptionsConfigurator : AdapterOptionsConfigurator<QdrantOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Qdrant";

    public QdrantOptionsConfigurator(
        IConfiguration config,
        ILogger<QdrantOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    public QdrantOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<QdrantOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(QdrantOptions options)
    {
        LogConfiguration(LogLevel.Debug, "initial",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode),
            ("connection", options.ConnectionString),
            ("endpoint", options.Endpoint));

        var endpoint = ReadProviderConfiguration(options.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint);

        var apiKey = ReadProviderConfiguration(options.ApiKey ?? "",
            Infrastructure.Constants.Configuration.Keys.ApiKey);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Qdrant",
            "ConnectionStrings:qdrant");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            LogConfiguration(LogLevel.Information, "explicit");
            options.ConnectionString = explicitConnectionString;
            options.Endpoint = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            LogConfiguration(LogLevel.Information, "auto");
            options.ConnectionString = ResolveAutonomousConnection(apiKey);
            options.Endpoint = options.ConnectionString;
        }
        else
        {
            LogConfiguration(LogLevel.Information, "preconfigured");
            options.Endpoint = options.ConnectionString;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
            options.ApiKey = apiKey;

        options.CollectionName = ReadProviderConfiguration(
            options.CollectionName ?? "",
            Infrastructure.Constants.Configuration.Keys.Collection,
            Infrastructure.Constants.Configuration.Keys.CollectionName);

        options.Distance = ReadProviderConfiguration(
            options.Distance,
            Infrastructure.Constants.Configuration.Keys.Distance,
            Infrastructure.Constants.Configuration.Keys.Metric);

        options.IdField = ReadProviderConfiguration(
            options.IdField,
            Infrastructure.Constants.Configuration.Keys.IdField);

        options.VectorField = ReadProviderConfiguration(
            options.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorFieldName);

        options.MetadataField = ReadProviderConfiguration(
            options.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataFieldName);

        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds);

        if (int.TryParse(ReadProviderConfiguration("", Infrastructure.Constants.Configuration.Keys.Dimension), out var dimension))
            options.Dimension = dimension;

        options.AutoCreateCollection = ReadProviderConfiguration(
            options.AutoCreateCollection,
            Infrastructure.Constants.Configuration.Keys.AutoCreate,
            Infrastructure.Constants.Configuration.Keys.AutoCreateCollection);

        options.WaitForResult = ReadProviderConfiguration(
            options.WaitForResult,
            Infrastructure.Constants.Configuration.Keys.WaitForResult);

        options.OnDisk = ReadProviderConfiguration(
            options.OnDisk,
            Infrastructure.Constants.Configuration.Keys.OnDisk);

        LogConfiguration(LogLevel.Information, "final",
            ("connection", options.ConnectionString),
            ("endpoint", options.Endpoint),
            ("distance", options.Distance),
            ("dimension", options.Dimension),
            ("waitForResult", options.WaitForResult));
    }

    private string ResolveAutonomousConnection(string? apiKey)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                LogDiscovery(LogLevel.Information, "disabled", ("fallback", "http://localhost:6333"));
                return "http://localhost:6333";
            }

            if (_discoveryCoordinator == null)
            {
                LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", "http://localhost:6333"));
                return "http://localhost:6333";
            }

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                context.Parameters["apiKey"] = apiKey;

            var discoveryTask = _discoveryCoordinator.DiscoverService("qdrant", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }

            LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", "http://localhost:6333"));
            return "http://localhost:6333";
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", "http://localhost:6333"));
            return "http://localhost:6333";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }
}
