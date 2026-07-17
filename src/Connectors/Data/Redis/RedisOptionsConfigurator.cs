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
        LogConfiguration(LogLevel.Debug, "initial",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode),
            ("connection", options.ConnectionString));

        // Redis-specific configuration
        var database = ReadProviderConfiguration(options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        var password = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.Password,
            Infrastructure.Constants.Configuration.Keys.AltPassword);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Discovery.EnvRedisUrl,
            Infrastructure.Constants.Discovery.EnvRedisConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            "ConnectionStrings:Redis");

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            LogConfiguration(LogLevel.Information, "explicit");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            LogConfiguration(LogLevel.Information, "auto");
            options.ConnectionString = ResolveAutonomousConnection(database, password);
        }
        else
        {
            LogConfiguration(LogLevel.Information, "preconfigured");
        }

        // Configure other Redis-specific options
        options.Database = ReadProviderConfiguration(
            options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        options.DefaultPageSize = ReadProviderConfiguration(
            options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        LogConfiguration(LogLevel.Information, "final",
            ("connection", options.ConnectionString),
            ("database", database));
    }

    private string ResolveAutonomousConnection(
        int? database,
        string? password)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                LogDiscovery(LogLevel.Information, "disabled", ("fallback", "localhost:6379"));
                return BuildRedisConnectionString("localhost", 6379, database, password);
            }

            if (_discoveryCoordinator == null)
            {
                LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", "localhost:6379"));
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
            var discoveryTask = _discoveryCoordinator.DiscoverService("redis", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }
            else
            {
                LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", "localhost:6379"));
                return BuildRedisConnectionString("localhost", 6379, database, password);
            }
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", "localhost:6379"));
            return BuildRedisConnectionString("localhost", 6379, database, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
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
