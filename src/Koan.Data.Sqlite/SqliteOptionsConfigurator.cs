using System;
using System.Collections.Generic;
using System.IO;
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

namespace Koan.Data.Sqlite;

/// <summary>
/// SQLite configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class SqliteOptionsConfigurator : AdapterOptionsConfigurator<SqliteOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Sqlite";

    public SqliteOptionsConfigurator(
        IConfiguration config,
        ILogger<SqliteOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public SqliteOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<SqliteOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(SqliteOptions options)
    {
        Logger?.LogInformation("SQLite Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
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
            options.ConnectionString = ResolveAutonomousConnection(Logger);
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
        }

        // Configure other SQLite-specific options
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

        Logger?.LogInformation("Final SQLite Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("SQLite Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using local database");
                return BuildSqliteConnectionString("Data/Koan.sqlite");
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to local database");
                return BuildSqliteConnectionString("Data/Koan.sqlite");
            }

            // Create discovery context with SQLite-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("sqlite", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("SQLite discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous SQLite discovery failed, falling back to local database");
                return BuildSqliteConnectionString("Data/Koan.sqlite");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous SQLite discovery, falling back to local database");
            return BuildSqliteConnectionString("Data/Koan.sqlite");
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Sqlite:DisableAutoDetection", false);
    }

    private static string BuildSqliteConnectionString(string filePath)
    {
        // Ensure directory exists for SQLite file
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={filePath}";
    }
}