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

namespace Koan.Data.Connector.Redis;

/// <summary>
/// Redis configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class RedisOptionsConfigurator : AdapterOptionsConfigurator<RedisOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Redis";

    public RedisOptionsConfigurator(
        IConfiguration config,
        ILogger<RedisOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public RedisOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<RedisOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(RedisOptions options)
    {
        Logger?.LogInformation("Redis Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        // Redis-specific configuration
        var database = ReadProviderConfiguration(options.Database,
            "Koan:Data:Redis:Database",
            "Koan:Data:Database");

        var password = ReadProviderConfiguration("",
            "Koan:Data:Redis:Password",
            "Koan:Data:Password");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Discovery.EnvRedisUrl,
            Infrastructure.Constants.Discovery.EnvRedisConnectionString,
            "Koan:Data:Redis:ConnectionString",
            "ConnectionStrings:Redis");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(database, password, Logger);
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
        }

        // Configure other Redis-specific options
        options.Database = ReadProviderConfiguration(
            options.Database,
            "Koan:Data:Redis:Database",
            "Koan:Data:Database");

        options.DefaultPageSize = ReadProviderConfiguration(
            options.DefaultPageSize,
            "Koan:Data:Redis:DefaultPageSize",
            "Koan:Data:DefaultPageSize");

        options.MaxPageSize = ReadProviderConfiguration(
            options.MaxPageSize,
            "Koan:Data:Redis:MaxPageSize",
            "Koan:Data:MaxPageSize");

        if (options.DefaultPageSize > options.MaxPageSize) options.DefaultPageSize = options.MaxPageSize;

        Logger?.LogInformation("Final Redis Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Database: {Database}", database);
        Logger?.LogInformation("Redis Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(
        int? database,
        string? password,
        ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return BuildRedisConnectionString("localhost", 6379, database, password);
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return BuildRedisConnectionString("localhost", 6379, database, password);
            }

            // Create discovery context with Redis-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (database.HasValue)
                context.Parameters["database"] = database.Value;
            if (!string.IsNullOrWhiteSpace(password))
                context.Parameters["password"] = password;

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("redis", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Redis discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous Redis discovery failed, falling back to localhost");
                return BuildRedisConnectionString("localhost", 6379, database, password);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Redis discovery, falling back to localhost");
            return BuildRedisConnectionString("localhost", 6379, database, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Redis:DisableAutoDetection", false);
    }

    private static string BuildRedisConnectionString(string hostname, int port, int? database, string? password)
    {
        var connectionString = $"{hostname}:{port}";
        if (!string.IsNullOrWhiteSpace(password))
        {
            connectionString += $",password={password}";
        }
        if (database.HasValue && database.Value != 0)
        {
            connectionString += $",defaultDatabase={database.Value}";
        }
        return connectionString;
    }
}
