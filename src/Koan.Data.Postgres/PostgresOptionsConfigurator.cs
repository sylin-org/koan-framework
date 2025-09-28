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

namespace Koan.Data.Postgres;

/// <summary>
/// PostgreSQL configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class PostgresOptionsConfigurator : AdapterOptionsConfigurator<PostgresOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Postgres";

    public PostgresOptionsConfigurator(
        IConfiguration config,
        ILogger<PostgresOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public PostgresOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<PostgresOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(PostgresOptions options)
    {
        Logger?.LogInformation("PostgreSQL Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        // PostgreSQL-specific configuration
        var databaseName = ReadProviderConfiguration(options.SearchPath ?? "public",
            "Koan:Data:Postgres:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        var username = ReadProviderConfiguration("postgres",
            "Koan:Data:Postgres:Username",
            "Koan:Data:Username");

        var password = ReadProviderConfiguration("postgres",
            "Koan:Data:Postgres:Password",
            "Koan:Data:Password");

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
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

        // Configure other PostgreSQL-specific options
        options.DefaultPageSize = ReadProviderConfiguration(
            options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = ReadProviderConfiguration(
            options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

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

        Logger?.LogInformation("Final PostgreSQL Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Database: {Database}", databaseName);
        Logger?.LogInformation("PostgreSQL Orchestration-Aware Configuration Complete");
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
                return BuildPostgresConnectionString("localhost", 5432, databaseName, username, password);
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return BuildPostgresConnectionString("localhost", 5432, databaseName, username, password);
            }

            // Create discovery context with PostgreSQL-specific parameters
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
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("postgres", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("PostgreSQL discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous PostgreSQL discovery failed, falling back to localhost");
                return BuildPostgresConnectionString("localhost", 5432, databaseName, username, password);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous PostgreSQL discovery, falling back to localhost");
            return BuildPostgresConnectionString("localhost", 5432, databaseName, username, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Postgres:DisableAutoDetection", false);
    }

    private static string BuildPostgresConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        return $"Host={hostname};Port={port};Database={database ?? "Koan"};Username={username ?? "postgres"};Password={password ?? "postgres"}";
    }
}