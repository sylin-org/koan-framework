using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Mongo;

/// <summary>
/// Orchestration-aware MongoDB configuration using centralized service discovery.
/// Replaces custom auto-detection with unified Koan orchestration patterns.
/// </summary>
internal sealed class MongoOptionsConfigurator(IConfiguration config, ILogger<MongoOptionsConfigurator> logger) : IConfigureOptions<MongoOptions>
{
    public void Configure(MongoOptions options)
    {
        logger.LogInformation("MongoDB Orchestration-Aware Configuration Started");
        logger.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        logger.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Database: '{Database}'",
            options.ConnectionString, options.Database);

        // Get database name and credentials for connection string construction
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

        // Use centralized orchestration-aware service discovery
        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(config, null);

        // Check for explicit connection string first
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
        // Configure other options
        options.Database = Configuration.ReadFirst(
            config,
            defaultValue: options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        // Paging guardrails
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

        // Final connection string normalization and logging
        options.ConnectionString = NormalizeConnectionString(options.ConnectionString);
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
            // Check if auto-detection is explicitly disabled
            if (IsAutoDetectionDisabled())
            {
                logger.LogInformation("Auto-detection disabled via configuration - using localhost");
                return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
            }

            // Create service discovery options with MongoDB-specific health checking
            var discoveryOptions = ServiceDiscoveryExtensions.ForMongoDB(databaseName, username, password);

            // Add MongoDB-specific health checking
            discoveryOptions = discoveryOptions with
            {
                HealthCheck = new HealthCheckOptions
                {
                    CustomHealthCheck = async (connectionString, ct) =>
                    {
                        var normalizedConnection = NormalizeConnectionString(connectionString);
                        return TryMongoPing(normalizedConnection, TimeSpan.FromMilliseconds(500));
                    },
                    Timeout = TimeSpan.FromMilliseconds(500),
                    Required = !KoanEnv.IsProduction // Less strict in production
                },
                AdditionalCandidates = GetAdditionalCandidatesFromEnvironment()
            };

            // Use centralized service discovery
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

        // Legacy environment variable support for backward compatibility
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
            // ping admin
            client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch { return false; }
    }

    // Container detection uses KoanEnv static runtime snapshot per ADR-0039
}