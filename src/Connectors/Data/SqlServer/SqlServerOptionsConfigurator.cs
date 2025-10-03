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

namespace Koan.Data.Connector.SqlServer;

/// <summary>
/// SQL Server configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class SqlServerOptionsConfigurator : AdapterOptionsConfigurator<SqlServerOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "SqlServer";

    public SqlServerOptionsConfigurator(
        IConfiguration config,
        ILogger<SqlServerOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public SqlServerOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<SqlServerOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(SqlServerOptions options)
    {
        Logger?.LogInformation("SQL Server Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        // SQL Server-specific configuration
        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
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

        // Configure other SQL Server-specific options
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

        // Serialization/materialization options
        options.JsonCaseInsensitive = Koan.Core.Configuration.Read(
            Configuration,
            Infrastructure.Constants.Configuration.Keys.JsonCaseInsensitive,
            options.JsonCaseInsensitive);
        options.JsonWriteIndented = Koan.Core.Configuration.Read(
            Configuration,
            Infrastructure.Constants.Configuration.Keys.JsonWriteIndented,
            options.JsonWriteIndented);
        options.JsonIgnoreNullValues = Koan.Core.Configuration.Read(
            Configuration,
            Infrastructure.Constants.Configuration.Keys.JsonIgnoreNullValues,
            options.JsonIgnoreNullValues);

        Logger?.LogInformation("Final SQL Server Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("SQL Server Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return BuildSqlServerConnectionString("localhost", 1433);
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
                return BuildSqlServerConnectionString("localhost", 1433);
            }

            // Create discovery context with SQL Server-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            // Add default SQL Server parameters
            context.Parameters["database"] = "Koan";
            context.Parameters["userId"] = "sa";
            context.Parameters["password"] = "Your_password123";

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("mssql", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("SQL Server discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous SQL Server discovery failed, falling back to localhost");
                return BuildSqlServerConnectionString("localhost", 1433);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous SQL Server discovery, falling back to localhost");
            return BuildSqlServerConnectionString("localhost", 1433);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:SqlServer:DisableAutoDetection", false);
    }

    private static string BuildSqlServerConnectionString(string hostname, int port)
    {
        return $"Server={hostname},{port};Database=Koan;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
    }
}
