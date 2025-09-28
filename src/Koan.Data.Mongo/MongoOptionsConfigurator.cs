using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Mongo;

/// <summary>
/// Orchestration-aware MongoDB configuration using centralized service discovery.
/// Inherits from AdapterOptionsConfigurator to eliminate configuration duplication.
/// </summary>
internal sealed class MongoOptionsConfigurator : AdapterOptionsConfigurator<MongoOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Mongo";

    public MongoOptionsConfigurator(
        IConfiguration config,
        ILogger<MongoOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    protected override void ConfigureProviderSpecific(MongoOptions options)
    {
        Logger?.LogInformation("MongoDB Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Database: '{Database}'",
            options.ConnectionString, options.Database);

        // MongoDB-specific configuration
        var databaseName = ReadProviderConfiguration(options.Database,
            "Koan:Data:Mongo:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        var username = ReadProviderConfiguration("",
            "Koan:Data:Mongo:Username",
            "Koan:Data:Username");

        var password = ReadProviderConfiguration("",
            "Koan:Data:Mongo:Password",
            "Koan:Data:Password");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(databaseName, username, password, Logger);
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
        }

        options.Database = ReadProviderConfiguration(options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        Logger?.LogInformation("Final MongoDB Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Database: {Database}", options.Database);
        Logger?.LogInformation("MongoDB Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(
        string? databaseName,
        string? username,
        string? password,
        ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
            }

            // Create discovery context with MongoDB-specific parameters
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

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("mongo", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("MongoDB discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous MongoDB discovery failed, falling back to localhost");
                return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous MongoDB discovery, falling back to localhost");
            return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Mongo:DisableAutoDetection", false);
    }

    private static string BuildMongoConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
        return $"mongodb://{auth}{hostname}:{port}{db}";
    }
}