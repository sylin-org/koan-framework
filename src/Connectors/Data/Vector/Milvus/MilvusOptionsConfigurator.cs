
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

namespace Koan.Data.Vector.Connector.Milvus;

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
            Infrastructure.Constants.Configuration.Keys.Endpoint);

        var databaseName = ReadProviderConfiguration(options.DatabaseName,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.DatabaseName);

        var username = ReadProviderConfiguration(options.Username ?? "",
            Infrastructure.Constants.Configuration.Keys.Username);

        var password = ReadProviderConfiguration(options.Password ?? "",
            Infrastructure.Constants.Configuration.Keys.Password);

        var token = ReadProviderConfiguration(options.Token ?? "",
            Infrastructure.Constants.Configuration.Keys.Token);

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
            Infrastructure.Constants.Configuration.Keys.Collection,
            Infrastructure.Constants.Configuration.Keys.CollectionName);
        options.PrimaryFieldName = ReadProviderConfiguration(
            options.PrimaryFieldName,
            Infrastructure.Constants.Configuration.Keys.PrimaryField,
            Infrastructure.Constants.Configuration.Keys.PrimaryFieldName);
        options.VectorFieldName = ReadProviderConfiguration(
            options.VectorFieldName,
            Infrastructure.Constants.Configuration.Keys.VectorField,
            Infrastructure.Constants.Configuration.Keys.VectorFieldName);
        options.MetadataFieldName = ReadProviderConfiguration(
            options.MetadataFieldName,
            Infrastructure.Constants.Configuration.Keys.MetadataField,
            Infrastructure.Constants.Configuration.Keys.MetadataFieldName);
        options.Metric = ReadProviderConfiguration(
            options.Metric,
            Infrastructure.Constants.Configuration.Keys.Metric);
        options.ConsistencyLevel = ReadProviderConfiguration(
            options.ConsistencyLevel,
            Infrastructure.Constants.Configuration.Keys.Consistency,
            Infrastructure.Constants.Configuration.Keys.ConsistencyLevel);
        options.DefaultTimeoutSeconds = ReadProviderConfiguration(
            options.DefaultTimeoutSeconds,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds);

        if (int.TryParse(ReadProviderConfiguration("", Infrastructure.Constants.Configuration.Keys.Dimension), out var dimension))
            options.Dimension = dimension;

        options.AutoCreateCollection = ReadProviderConfiguration(
            options.AutoCreateCollection,
            Infrastructure.Constants.Configuration.Keys.AutoCreate,
            Infrastructure.Constants.Configuration.Keys.AutoCreateCollection);

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
            var discoveryTask = _discoveryCoordinator.DiscoverService("milvus", context);
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
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }
}

