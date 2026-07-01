using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Logging;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.ZenGarden.Core;
using MongoItems = Koan.Data.Connector.Mongo.Infrastructure.MongoProvenanceItems;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// Orchestration-aware MongoDB configuration using centralized service discovery.
/// Inherits from AdapterOptionsConfigurator to eliminate configuration duplication.
/// </summary>
internal sealed class MongoOptionsConfigurator : AdapterOptionsConfigurator<MongoOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;
    private readonly IZenGardenInitializationProvider? _zenGardenInitializationProvider;

    protected override string ProviderName => "Mongo";

    public MongoOptionsConfigurator(
        IConfiguration config,
        ILogger<MongoOptionsConfigurator>? logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null,
        IZenGardenInitializationProvider? zenGardenInitializationProvider = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
        _zenGardenInitializationProvider = zenGardenInitializationProvider;
    }

    protected override void ConfigureProviderSpecific(MongoOptions options)
    {
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Start);
        KoanLog.ConfigDebug(Logger, LogActions.Config, "context",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode));
        KoanLog.ConfigDebug(Logger, LogActions.Config, "initial-options",
            ("connection", options.ConnectionString ?? "(null)"),
            ("database", options.Database ?? "(null)"));

        // MongoDB-specific configuration
        var databaseName = ReadProviderConfiguration(options.Database,
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Database),
            Infrastructure.ConfigurationConstants.DataFallback.Database,
            "ConnectionStrings:Database");

        var username = ReadProviderConfiguration("",
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Username),
            Infrastructure.ConfigurationConstants.DataFallback.Username);

        var password = ReadProviderConfiguration("",
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Password),
            Infrastructure.ConfigurationConstants.DataFallback.Password);

        var configuredConnectionString = ReadProviderConfiguration(
            "",
            MongoItems.ConnectionStringKeys);

        var requestedConnection = !string.IsNullOrWhiteSpace(configuredConnectionString)
            ? configuredConnectionString
            : options.ConnectionString;

        if (ZenGardenConnectionIntent.TryParse(requestedConnection, out var zenGardenIntent))
        {
            if (TryResolveZenGardenConnection(zenGardenIntent!, databaseName, username, password, out var resolved))
            {
                options.ConnectionString = resolved;
                KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "intent-resolved", ("intent", requestedConnection));
            }
            else
            {
                options.ConnectionString = ResolveAutonomousConnection(databaseName, username, password);
                KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "intent-fallback-autonomous", ("intent", requestedConnection));
            }
        }
        else if (IsAutoConnection(requestedConnection))
        {
            // Discovery mode: always run the health-checked candidate probe. Zen Garden (if referenced)
            // contributes its resolved offering endpoint into that probe as ONE health-checked candidate — it no
            // longer short-circuits here, so an unreachable ZG answer (e.g. a same-host offering advertised at the
            // docker bridge gateway for a loopback-bound host MongoDB) falls through to the compose-name /
            // host.docker.internal / localhost candidates instead of stranding the app.
            KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-mode",
                ("database", databaseName ?? "(none)"));
            options.ConnectionString = ResolveAutonomousConnection(databaseName, username, password);
        }
        else
        {
            options.ConnectionString = requestedConnection ?? "";
            KoanLog.ConfigInfo(Logger, LogActions.Config, "connection-preconfigured");
        }

        options.Database = ReadProviderConfiguration(options.Database,
            MongoItems.DatabaseKeys)
            ?? options.Database
            ?? "";

        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomeValues.Final,
            ("connection", options.ConnectionString ?? "(null)"),
            ("database", options.Database ?? "(null)"));
        KoanLog.ConfigInfo(Logger, LogActions.Config, LogOutcomes.Complete);
    }

    private string ResolveAutonomousConnection(
        string? databaseName,
        string? username,
        string? password)
    {
        var fallback = MongoConnectionString.Build("localhost", 27017, databaseName, username, password);
        try
        {
            if (IsAutoDetectionDisabled())
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, "auto-disabled", ("fallback", fallback));
                return fallback;
            }

            if (_discoveryCoordinator == null)
            {
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, "coordinator-missing", ("fallback", fallback));
                return fallback;
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

            KoanLog.ConfigDebug(Logger, LogActions.DiscoveryRequest, LogOutcomes.Start,
                ("mode", context.OrchestrationMode.ToString()),
                ("database", databaseName ?? "(none)"),
                ("user", username ?? "(none)"));

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverService("mongo", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                KoanLog.ConfigInfo(Logger, LogActions.Discovery, LogOutcomeValues.Success,
                    ("url", result.ServiceUrl),
                    ("method", result.DiscoveryMethod),
                    ("healthy", result.IsHealthy));
                return result.ServiceUrl;
            }
            else
            {
                KoanLog.ConfigWarning(Logger, LogActions.Discovery, LogOutcomeValues.Failed,
                    ("reason", result.ErrorMessage ?? "unknown"),
                    ("fallback", fallback));
                return fallback;
            }
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(Logger, LogActions.Discovery, "exception",
                ("reason", ex.Message),
                ("fallback", fallback));
            KoanLog.ConfigDebug(Logger, LogActions.Discovery, "exception-detail", ("exception", ex.ToString()));
            return fallback;
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.DisableAutoDetection), false);
    }

    private bool TryResolveZenGardenConnection(
        ZenGardenConnectionIntent intent,
        string? databaseName,
        string? username,
        string? password,
        out string connectionString)
    {
        connectionString = "";
        if (_zenGardenInitializationProvider is null)
        {
            KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "provider-missing");
            return false;
        }

        try
        {
            var resolved = _zenGardenInitializationProvider
                .Resolve(intent)
                .GetAwaiter()
                .GetResult();

            if (resolved is null)
            {
                KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "offering-not-ready",
                    ("offering", intent.ToOfferingSelector()));
                return false;
            }

            if (!TryBuildMongoConnectionString(
                    resolved,
                    databaseName,
                    username,
                    password,
                    out connectionString))
            {
                KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "missing-endpoint",
                    ("offering", resolved.ToolFqid));
                return false;
            }

            KoanLog.ConfigInfo(Logger, LogActions.ZenGarden, "resolved",
                ("offering", resolved.ToolFqid),
                ("connection", connectionString));
            return true;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigWarning(Logger, LogActions.ZenGarden, "exception",
                ("offering", intent.ToOfferingSelector()),
                ("reason", ex.Message));
            KoanLog.ConfigDebug(Logger, LogActions.ZenGarden, "exception-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private static bool TryBuildMongoConnectionString(
        ZenGardenOfferingResolution resolved,
        string? databaseName,
        string? username,
        string? password,
        out string connectionString)
    {
        connectionString = "";

        // Primary path: native MongoDB connection string (supports replica sets with comma-separated hosts).
        // Uses GetConnectionString (prefix match) instead of GetUri (Uri.TryCreate) because
        // System.Uri rejects multi-host MongoDB connection strings as invalid RFC 3986 URIs.
        var mongoConnectionString = resolved.GetConnectionString("mongodb", "mongodb+srv");
        if (!string.IsNullOrWhiteSpace(mongoConnectionString) &&
            mongoConnectionString.StartsWith("mongodb", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = MongoConnectionString.MergeOverrides(mongoConnectionString, databaseName, username, password);
            return true;
        }

        var genericUri = resolved.GetUri("http", "https", "tcp", "udp");
        if (!string.IsNullOrWhiteSpace(genericUri) &&
            Uri.TryCreate(genericUri, UriKind.Absolute, out var parsedGeneric) &&
            !string.IsNullOrWhiteSpace(parsedGeneric.Host))
        {
            var port = parsedGeneric.IsDefaultPort || parsedGeneric.Port <= 0
                ? resolved.Port ?? 27017
                : parsedGeneric.Port;
            connectionString = MongoConnectionString.Build(parsedGeneric.Host, port, databaseName, username, password);
            return true;
        }

        var host = !string.IsNullOrWhiteSpace(resolved.Hostname)
            ? resolved.Hostname
            : resolved.Ip;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        connectionString = MongoConnectionString.Build(host, resolved.Port ?? 27017, databaseName, username, password);
        return true;
    }

    private static bool IsAutoConnection(string? connectionString)
    {
        return string.IsNullOrWhiteSpace(connectionString)
            || string.Equals(connectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static class LogActions
    {
        public const string Config = "mongo.config";
        public const string Discovery = "mongo.discovery";
        public const string DiscoveryRequest = "mongo.discovery.request";
        public const string ZenGarden = "mongo.zengarden";
    }

    private static class LogOutcomeValues
    {
        public const string Final = "final";
        public const string Success = "success";
        public const string Failed = "failed";
    }
}
