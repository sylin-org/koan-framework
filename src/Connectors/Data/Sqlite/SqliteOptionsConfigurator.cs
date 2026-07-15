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
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Sqlite;

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
        ILogger<SqliteOptionsConfigurator>? logger,
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
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Start);
        KoanLog.ConfigInfo(Logger, LogActions.Config, "context",
            ("environment", KoanEnv.EnvironmentName),
            ("mode", KoanEnv.OrchestrationMode.ToString()));
        KoanLog.ConfigDebug(Logger, LogActions.Config, "initial",
            ("connection", Redaction.DeIdentify(options.ConnectionString)));

        var explicitConnectionString = Infrastructure.SqliteConnectionConfiguration
            .ReadProviderFallback(Configuration);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString) && !IsAuto(explicitConnectionString))
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "explicit",
                ("source", "configuration"));
            options.ConnectionString = explicitConnectionString;
        }
        else if (IsAuto(explicitConnectionString) || IsAuto(options.ConnectionString) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto");
            options.ConnectionString = ResolveAutonomousConnection(Logger);
        }
        else
        {
            KoanLog.ConfigInfo(Logger, LogActions.Config, "preconfigured");
        }

        // Configure other SQLite-specific options
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

        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomeValues.Final,
            ("connection", Redaction.DeIdentify(options.ConnectionString)));
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Complete);
    }

    private string ResolveAutonomousConnection(ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                KoanLog.ConfigInfo(logger, LogActions.Discovery, "disabled",
                    ("reason", "config"));
                return BuildSqliteConnectionString(".koan/data/Koan.sqlite");
            }

            if (_discoveryCoordinator == null)
            {
                KoanLog.ConfigWarning(logger, LogActions.Discovery, LogOutcomeValues.Fallback,
                    ("reason", "no-coordinator"));
                return BuildSqliteConnectionString(".koan/data/Koan.sqlite");
            }

            // Create discovery context with SQLite-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                // Target selection is configuration, not readiness. Repository use and the active health contributor
                // own authoritative I/O, so an available-but-unelected connector cannot create a database here.
                RequireHealthValidation = false,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverService("sqlite", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                KoanLog.ConfigInfo(logger, LogActions.Discovery, LogOutcomeValues.Success,
                    ("url", Redaction.DeIdentify(result.ServiceUrl)));
                return result.ServiceUrl;
            }
            else
            {
                KoanLog.ConfigWarning(logger, LogActions.Discovery, LogOutcomeValues.Fallback,
                    ("reason", "no-candidate"));
                return BuildSqliteConnectionString(".koan/data/Koan.sqlite");
            }
        }
        catch (Exception ex)
        {
            KoanLog.ConfigError(logger, LogActions.Discovery, "exception",
                ("error", Redaction.DeIdentify(ex.Message)));
            return BuildSqliteConnectionString(".koan/data/Koan.sqlite");
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }

    private static string BuildSqliteConnectionString(string filePath)
        => $"Data Source={filePath}";

    private static bool IsAuto(string? value)
        => Infrastructure.SqliteConnectionConfiguration.IsAuto(value);

    private static class LogActions
    {
        public const string Config = "sqlite.config";
        public const string Discovery = "sqlite.discovery";
    }

    private static class LogOutcomeValues
    {
        public const string Final = "final";
        public const string Success = "success";
        public const string Fallback = "fallback";
    }
}
