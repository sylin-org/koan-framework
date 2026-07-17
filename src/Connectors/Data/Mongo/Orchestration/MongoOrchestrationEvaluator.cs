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
        var connectionString = Configuration.ReadFirst(configuration, "",
            MongoItems.ConnectionStringKeys);

        return !string.IsNullOrWhiteSpace(connectionString);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit connection strings (not "auto")
        var connectionString = Configuration.ReadFirst(configuration, "",
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
                .Select(MongoConnectionString.ExtractHost)
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
            ReportCredentialValidation("start", ("host", hostResult.HostEndpoint));

            // Get configured credentials
            var databaseName = GetDatabaseName(configuration);
            var username = Configuration.ReadFirst(configuration, "",
                Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Username),
                Infrastructure.ConfigurationConstants.DataFallback.Username);
            var password = Configuration.ReadFirst(configuration, "",
                Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Password),
                Infrastructure.ConfigurationConstants.DataFallback.Password);

            // Build connection string for validation
            var connectionString = MongoConnectionString.Build(hostResult.HostEndpoint!, databaseName, username, password);

            // Try to connect with the configured credentials
            var isValid = await Task.Run(() => TryMongoPing(connectionString, TimeSpan.FromMilliseconds(1000)));

            ReportCredentialValidation(isValid ? "accepted" : "rejected");
            return isValid;
        }
        catch (Exception ex)
        {
            ReportCredentialValidation("failed", ("error", ex));
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptor(IConfiguration configuration, OrchestrationContext context)
    {
        // Get configuration values
        var databaseName = GetDatabaseName(configuration);
        var username = Configuration.ReadFirst(configuration, "root",
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Username),
            Infrastructure.ConfigurationConstants.DataFallback.Username);
        var password = Configuration.ReadFirst(configuration, "mongodb",
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Password),
            Infrastructure.ConfigurationConstants.DataFallback.Password);

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
            Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Database),
            Infrastructure.ConfigurationConstants.DataFallback.Database,
            "ConnectionStrings:Database");
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
