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
using Koan.Data.Connector.Postgres; // SchemaDdlPolicy / SchemaMatchingMode (pg-wire reuse)

namespace Koan.Data.Connector.Cockroach;

/// <summary>
/// CockroachDB configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class CockroachOptionsConfigurator : AdapterOptionsConfigurator<CockroachOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Cockroach";

    public CockroachOptionsConfigurator(
        IConfiguration config,
        ILogger<CockroachOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public CockroachOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<CockroachOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(CockroachOptions options)
    {
        Logger?.LogInformation("CockroachDB Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        // CockroachDB-specific configuration
        var databaseName = ReadProviderConfiguration(options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.DataFallback.Database,
            "ConnectionStrings:Database");

        var username = ReadProviderConfiguration("cockroach",
            Infrastructure.Constants.Configuration.Keys.Username,
            Infrastructure.Constants.Configuration.DataFallback.Username);

        var password = ReadProviderConfiguration("cockroach",
            Infrastructure.Constants.Configuration.Keys.Password,
            Infrastructure.Constants.Configuration.DataFallback.Password);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsCockroach,
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

        // Configure other CockroachDB-specific options
        options.DefaultPageSize = ReadProviderConfiguration(
            options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        var ddlStr = ReadProviderConfiguration(options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = ReadProviderConfiguration(options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Koan.Core.Configuration.Read(
            Configuration,
            Constants.Configuration.Koan.AllowMagicInProduction,
            options.AllowProductionDdl);

        options.SearchPath = ReadProviderConfiguration(options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);

        Logger?.LogInformation("Final CockroachDB Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Database: {Database}", databaseName);
        Logger?.LogInformation("CockroachDB Orchestration-Aware Configuration Complete");
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
                return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
            }

            // Create discovery context with CockroachDB-specific parameters
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
            var discoveryTask = _discoveryCoordinator.DiscoverService("cockroach", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("CockroachDB discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous CockroachDB discovery failed, falling back to localhost");
                return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous CockroachDB discovery, falling back to localhost");
            return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }

    private static string BuildCockroachConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        return $"Host={hostname};Port={port};Database={database ?? "Koan"};Username={username ?? "root"};Password={password ?? ""}";
    }
}
