
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

namespace Koan.Data.Milvus;

/// <summary>
/// Milvus configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class MilvusOptionsConfigurator : AdapterOptionsConfigurator<MilvusOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Milvus";

    public MilvusOptionsConfigurator(
        IConfiguration config,
        ILogger<MilvusOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public MilvusOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<MilvusOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(MilvusOptions options)
    {
        Logger?.LogInformation("Milvus Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Endpoint: '{Endpoint}'",
            options.ConnectionString, options.Endpoint);

        // Read Milvus-specific configuration
        var endpoint = ReadProviderConfiguration(options.Endpoint,
            "Koan:Data:Milvus:Endpoint");

        var databaseName = ReadProviderConfiguration(options.DatabaseName,
            "Koan:Data:Milvus:Database",
            "Koan:Data:Milvus:DatabaseName");

        var username = ReadProviderConfiguration(options.Username ?? "",
            "Koan:Data:Milvus:Username");

        var password = ReadProviderConfiguration(options.Password ?? "",
            "Koan:Data:Milvus:Password");

        var token = ReadProviderConfiguration(options.Token ?? "",
            "Koan:Data:Milvus:Token");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Milvus",
            "ConnectionStrings:milvus");

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
            options.ConnectionString = ResolveAutonomousConnection(databaseName, username, password, token, Logger);
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
            options.Endpoint = options.ConnectionString; // For backward compatibility
        }

        // Apply other configuration
        if (!string.IsNullOrWhiteSpace(databaseName))
            options.DatabaseName = databaseName;
        if (!string.IsNullOrWhiteSpace(username))
            options.Username = username;
        if (!string.IsNullOrWhiteSpace(password))
            options.Password = password;
        if (!string.IsNullOrWhiteSpace(token))
            options.Token = token;

        // Configure Milvus-specific options
        options.CollectionName = ReadProviderConfiguration(
            options.CollectionName ?? "",
            "Koan:Data:Milvus:Collection",
            "Koan:Data:Milvus:CollectionName");
        options.PrimaryFieldName = ReadProviderConfiguration(
            options.PrimaryFieldName,
            "Koan:Data:Milvus:PrimaryField",
            "Koan:Data:Milvus:PrimaryFieldName");
        options.VectorFieldName = ReadProviderConfiguration(
            options.VectorFieldName,
            "Koan:Data:Milvus:VectorField",
            "Koan:Data:Milvus:VectorFieldName");
        options.MetadataFieldName = ReadProviderConfiguration(
            options.MetadataFieldName,
            "Koan:Data:Milvus:MetadataField",
            "Koan:Data:Milvus:MetadataFieldName");
        options.Metric = ReadProviderConfiguration(
            options.Metric,
            "Koan:Data:Milvus:Metric");
        options.ConsistencyLevel = ReadProviderConfiguration(
            options.ConsistencyLevel,
            "Koan:Data:Milvus:Consistency",
            "Koan:Data:Milvus:ConsistencyLevel");
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            "Koan:Data:Milvus:TimeoutSeconds");

        if (int.TryParse(ReadProviderConfiguration("", "Koan:Data:Milvus:Dimension"), out var dimension))
            options.Dimension = dimension;

        options.AutoCreateCollection = ReadProviderConfiguration(
            options.AutoCreateCollection,
            "Koan:Data:Milvus:AutoCreate",
            "Koan:Data:Milvus:AutoCreateCollection");

        Logger?.LogInformation("Final Milvus Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Endpoint: {Endpoint}", options.Endpoint);
        Logger?.LogInformation("Database: {Database}", options.DatabaseName);
        Logger?.LogInformation("Milvus Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(
        string? databaseName,
        string? username,
        string? password,
        string? token,
        ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "http://localhost:19530";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return "http://localhost:19530";
            }

            // Create discovery context with Milvus-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (!string.IsNullOrWhiteSpace(databaseName))
                context.Parameters["database"] = databaseName;
            if (!string.IsNullOrWhiteSpace(username))
                context.Parameters["username"] = username;
            if (!string.IsNullOrWhiteSpace(password))
                context.Parameters["password"] = password;
            if (!string.IsNullOrWhiteSpace(token))
                context.Parameters["token"] = token;

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("milvus", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Milvus discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous Milvus discovery failed, falling back to localhost");
                return "http://localhost:19530";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Milvus discovery, falling back to localhost");
            return "http://localhost:19530";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Milvus:DisableAutoDetection", false);
    }
}
