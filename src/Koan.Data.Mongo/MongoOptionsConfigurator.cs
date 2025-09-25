using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Orchestration;

namespace Koan.Data.Mongo;

/// <summary>
/// Orchestration-aware MongoDB configuration using centralized service discovery.
/// Replaces custom auto-detection with unified Koan orchestration patterns.
/// </summary>
internal sealed class MongoOptionsConfigurator(
    IConfiguration config,
    ILogger<MongoOptionsConfigurator> logger,
    IOptions<AdaptersReadinessOptions> readinessOptions) : IConfigureOptions<MongoOptions>
{
    private readonly AdaptersReadinessOptions _readinessDefaults = readinessOptions.Value;

    public void Configure(MongoOptions options)
    {
        logger.LogInformation("MongoDB Orchestration-Aware Configuration Started");
        logger.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        logger.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Database: '{Database}'",
            options.ConnectionString, options.Database);

        var databaseName = Configuration.ReadFirst(config, "KoanAspireDemo",
            "Koan:Data:Mongo:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        var username = Configuration.ReadFirst(config, "",
            "Koan:Data:Mongo:Username",
            "Koan:Data:Username");

        var password = Configuration.ReadFirst(config, "",
            "Koan:Data:Mongo:Password",
            "Koan:Data:Password");

        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(config, null);

        var explicitConnectionString = Configuration.ReadFirst(config, "",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            logger.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            logger.LogInformation("Auto-detection mode - using orchestration-aware service discovery");
            options.ConnectionString = ResolveOrchestrationAwareConnection(serviceDiscovery, databaseName, username, password, logger);
        }
        else
        {
            logger.LogInformation("Using pre-configured connection string");
        }

        options.Database = Configuration.ReadFirst(
            config,
            defaultValue: options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        options.DefaultPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        options.ConnectionString = NormalizeConnectionString(options.ConnectionString);

        var policyStr = Configuration.ReadFirst(config, options.Readiness.Policy.ToString(),
            "Koan:Data:Mongo:Readiness:Policy");
        if (Enum.TryParse<ReadinessPolicy>(policyStr, out var policy))
        {
            options.Readiness.Policy = policy;
        }

        var timeoutSecondsStr = Configuration.ReadFirst(config, ((int)options.Readiness.Timeout.TotalSeconds).ToString(),
            "Koan:Data:Mongo:Readiness:Timeout");
        if (int.TryParse(timeoutSecondsStr, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            var readinessTimeout = TimeSpan.FromSeconds(timeoutSeconds);
            options.Readiness.Timeout = readinessTimeout;
        }
        else if (options.Readiness.Timeout <= TimeSpan.Zero)
        {
            options.Readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        options.Readiness.EnableReadinessGating = Configuration.Read(config,
            "Koan:Data:Mongo:Readiness:EnableReadinessGating",
            options.Readiness.EnableReadinessGating);

        if (options.Readiness.Timeout <= TimeSpan.Zero)
        {
            options.Readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        logger.LogInformation("Final MongoDB Configuration");
        logger.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        logger.LogInformation("Database: {Database}", options.Database);
        logger.LogInformation("MongoDB Orchestration-Aware Configuration Complete");
    }

    private string ResolveOrchestrationAwareConnection(
        IOrchestrationAwareServiceDiscovery serviceDiscovery,
        string? databaseName,
        string? username,
        string? password,
        ILogger logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger.LogInformation("Auto-detection disabled via configuration - using localhost");
                return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
            }

            var discoveryOptions = ServiceDiscoveryExtensions.ForMongoDB(databaseName, username, password);

            discoveryOptions = discoveryOptions with
            {
                HealthCheck = new HealthCheckOptions
                {
                    CustomHealthCheck = (candidate, _) => Task.FromResult(TryMongoPing(NormalizeConnectionString(candidate), TimeSpan.FromMilliseconds(500))),
                    Timeout = TimeSpan.FromMilliseconds(500),
                    Required = !KoanEnv.IsProduction
                },
                AdditionalCandidates = GetAdditionalCandidatesFromEnvironment()
            };

            var discoveryTask = serviceDiscovery.DiscoverServiceAsync("mongodb", discoveryOptions);
            var result = discoveryTask.GetAwaiter().GetResult();

            logger.LogInformation("MongoDB discovered via {Method}: {ServiceUrl}",
                result.DiscoveryMethod, result.ServiceUrl);

            if (!result.IsHealthy && discoveryOptions.HealthCheck?.Required == true)
            {
                logger.LogWarning("Discovered MongoDB service failed health check but proceeding anyway");
            }

            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration-aware MongoDB discovery, falling back to localhost");
            return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Configuration.Read(config, "Koan:Data:Mongo:DisableAutoDetection", false)
               || Configuration.Read(config, "Koan_DATA_MONGO_DISABLE_AUTO_DETECTION", false);
    }

    private string[] GetAdditionalCandidatesFromEnvironment()
    {
        var candidates = new List<string>();
        var envList = Environment.GetEnvironmentVariable(MongoConstants.EnvList);
        if (!string.IsNullOrWhiteSpace(envList))
        {
            candidates.AddRange(envList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return candidates.ToArray();
    }

    private static string BuildMongoConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
        return $"mongodb://{auth}{hostname}:{port}{db}";
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "mongodb://" + trimmed;
    }

    private static bool TryMongoPing(string connectionString, TimeSpan timeout)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = timeout;
            var client = new MongoClient(settings);
            client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Container detection uses KoanEnv static runtime snapshot per ADR-0039
}
