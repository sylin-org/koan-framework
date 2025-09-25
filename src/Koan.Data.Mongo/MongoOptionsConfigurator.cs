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
using Koan.Core.Adapters.Configuration;
using Koan.Core.Orchestration;

namespace Koan.Data.Mongo;

/// <summary>
/// Orchestration-aware MongoDB configuration using centralized service discovery.
/// Inherits from AdapterOptionsConfigurator to eliminate configuration duplication.
/// </summary>
internal sealed class MongoOptionsConfigurator : AdapterOptionsConfigurator<MongoOptions>
{
    protected override string ProviderName => "Mongo";

    public MongoOptionsConfigurator(
        IConfiguration config,
        ILogger<MongoOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions)
        : base(config, logger, readinessOptions)
    {
    }

    protected override void ConfigureProviderSpecific(MongoOptions options)
    {
        Logger?.LogInformation("MongoDB Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Database: '{Database}'",
            options.ConnectionString, options.Database);

        // MongoDB-specific configuration
        var databaseName = ReadProviderConfiguration(options.Database,
            "Koan:Data:Mongo:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        var username = ReadProviderConfiguration("",
            "Koan:Data:Mongo:Username",
            "Koan:Data:Username");

        var password = ReadProviderConfiguration("",
            "Koan:Data:Mongo:Password",
            "Koan:Data:Password");

        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(Configuration, null);

        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using orchestration-aware service discovery");
            options.ConnectionString = ResolveOrchestrationAwareConnection(serviceDiscovery, databaseName, username, password, Logger);
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
        }

        options.Database = ReadProviderConfiguration(options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

        options.ConnectionString = NormalizeConnectionString(options.ConnectionString);

        Logger?.LogInformation("Final MongoDB Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Database: {Database}", options.Database);
        Logger?.LogInformation("MongoDB Orchestration-Aware Configuration Complete");
    }

    private string ResolveOrchestrationAwareConnection(
        IOrchestrationAwareServiceDiscovery serviceDiscovery,
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

            logger?.LogInformation("MongoDB discovered via {Method}: {ServiceUrl}",
                result.DiscoveryMethod, result.ServiceUrl);

            if (!result.IsHealthy && discoveryOptions.HealthCheck?.Required == true)
            {
                logger?.LogWarning("Discovered MongoDB service failed health check but proceeding anyway");
            }

            return result.ServiceUrl;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in orchestration-aware MongoDB discovery, falling back to localhost");
            return BuildMongoConnectionString("localhost", 27017, databaseName, username, password);
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Mongo:DisableAutoDetection", false)
               || Koan.Core.Configuration.Read(Configuration, "Koan_DATA_MONGO_DISABLE_AUTO_DETECTION", false);
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
}