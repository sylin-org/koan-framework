using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Data.Connector.Mongo.Infrastructure;
using MongoItems = Koan.Data.Connector.Mongo.Infrastructure.MongoProvenanceItems;

namespace Koan.Data.Connector.Mongo.Orchestration;

/// <summary>
/// MongoDB-specific orchestration evaluator that determines if MongoDB containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class MongoOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public MongoOrchestrationEvaluator(ILogger<MongoOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => "mongodb";
    public override int StartupPriority => 150; // After infrastructure, before application services

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // MongoDB is enabled if connection string is configured (including "auto")
        var connectionString = Configuration.ReadFirst(configuration, string.Empty,
            MongoItems.ConnectionStringKeys);

        return !string.IsNullOrWhiteSpace(connectionString);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit connection strings (not "auto")
        var connectionString = Configuration.ReadFirst(configuration, string.Empty,
            MongoItems.ConnectionStringKeys);

        return !string.IsNullOrWhiteSpace(connectionString) &&
               !string.Equals(connectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    protected override int GetDefaultPort()
    {
        return Constants.Discovery.DefaultPort; // 27017
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variable for additional hosts
        var envList = Environment.GetEnvironmentVariable(Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(envList))
        {
            var urls = envList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(ExtractHostFromConnectionString)
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .ToArray();

            candidates.AddRange(urls!);
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            Logger?.LogDebug("[MongoDB] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Get configured credentials
            var databaseName = GetDatabaseName(configuration);
            var username = Configuration.ReadFirst(configuration, "",
                "Koan:Data:Mongo:Username",
                "Koan:Data:Username");
            var password = Configuration.ReadFirst(configuration, "",
                "Koan:Data:Mongo:Password",
                "Koan:Data:Password");

            // Build connection string for validation
            var connectionString = BuildMongoConnectionString(hostResult.HostEndpoint!, databaseName, username, password);

            // Try to connect with the configured credentials
            var isValid = await Task.Run(() => TryMongoPing(connectionString, TimeSpan.FromMilliseconds(1000)));

            Logger?.LogDebug("[MongoDB] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[MongoDB] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Get configuration values
        var databaseName = GetDatabaseName(configuration);
        var username = Configuration.ReadFirst(configuration, "root",
            "Koan:Data:Mongo:Username",
            "Koan:Data:Username");
        var password = Configuration.ReadFirst(configuration, "mongodb",
            "Koan:Data:Mongo:Password",
            "Koan:Data:Password");

        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "mongodb",
            ["MONGO_INITDB_ROOT_USERNAME"] = username,
            ["MONGO_INITDB_ROOT_PASSWORD"] = password,
            ["MONGO_INITDB_DATABASE"] = databaseName
        };

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "mongo:8.0",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "mongosh --eval \"db.runCommand('ping').ok\" --quiet",
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-mongodb-{context.SessionId}:/data/db"
            }
        });
    }

    private string GetDatabaseName(IConfiguration configuration)
    {
        return Configuration.ReadFirst(configuration, "KoanDatabase",
            "Koan:Data:Mongo:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");
    }

    private static string BuildMongoConnectionString(string hostPort, string? databaseName, string? username, string? password)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "27017";

        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(databaseName) ? "" : $"/{databaseName}";
        return $"mongodb://{auth}{host}:{port}{db}";
    }

    private static string? ExtractHostFromConnectionString(string connectionString)
    {
        try
        {
            // Handle both mongodb:// and plain host:port formats
            if (connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Assume it's just host:port
            return connectionString;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryMongoPing(string connectionString, TimeSpan timeout)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = timeout;
            var client = new MongoClient(settings);
            // ping admin database
            client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
