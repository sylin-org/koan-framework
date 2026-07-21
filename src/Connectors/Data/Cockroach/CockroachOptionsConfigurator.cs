using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Core.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Relational.Orchestration;

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
        LogConfiguration(LogLevel.Debug, "initial",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode),
            ("connection", options.ConnectionString));

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
            LogConfiguration(LogLevel.Information, "explicit");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            LogConfiguration(LogLevel.Information, "auto");
            options.ConnectionString = ResolveAutonomousConnection(databaseName, username, password);
        }
        else
        {
            LogConfiguration(LogLevel.Information, "preconfigured");
        }

        // Configure other CockroachDB-specific options
        var ddlStr = ReadProviderConfiguration(options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<RelationalDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = ReadProviderConfiguration(options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<RelationalSchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Koan.Core.Configuration.Read(
            Configuration,
            Constants.Configuration.Koan.AllowMagicInProduction,
            options.AllowProductionDdl);

        options.SearchPath = ReadProviderConfiguration(options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);

        LogConfiguration(LogLevel.Information, "final",
            ("connection", options.ConnectionString),
            ("database", databaseName));
    }

    private string ResolveAutonomousConnection(
        string? databaseName,
        string? username,
        string? password)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                LogDiscovery(LogLevel.Information, "disabled", ("fallback", "localhost"));
                return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
            }

            if (_discoveryCoordinator == null)
            {
                LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", "localhost"));
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
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }
            else
            {
                LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", "localhost"));
                return BuildCockroachConnectionString("localhost", 26257, databaseName, username, password);
            }
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", "localhost"));
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
