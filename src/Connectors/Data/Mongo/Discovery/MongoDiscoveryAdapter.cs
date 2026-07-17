using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Mongo.Discovery;

/// <summary>
/// MongoDB autonomous discovery adapter.
/// Contains ALL MongoDB-specific knowledge - core orchestration knows nothing about MongoDB.
/// Reads own KoanServiceAttribute and handles MongoDB-specific health checks.
/// </summary>
internal sealed class MongoDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "mongo";
    public override string[] Aliases => new[] { "mongodb" };

    public MongoDiscoveryAdapter(IConfiguration configuration, ILogger<MongoDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>MongoDB adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(MongoAdapterFactory);

    /// <summary>MongoDB-specific health validation using MongoDB ping command</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        var settings = MongoClientSettings.FromConnectionString(serviceUrl);
        settings.ServerSelectionTimeout = context.HealthCheckTimeout;

        var client = new MongoClient(settings);
        await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1), cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>MongoDB adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check MongoDB-specific configuration paths
        return _configuration.GetConnectionString("MongoDB") ??
               _configuration[Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.ConnectionString)] ??
               _configuration[Infrastructure.ConfigurationConstants.DataFallback.ConnectionString];
    }

    /// <summary>MongoDB-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var mongoUrls = Environment.GetEnvironmentVariable("MONGO_URLS") ??
                       Environment.GetEnvironmentVariable("MONGODB_URLS");

        if (string.IsNullOrWhiteSpace(mongoUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return mongoUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(url => new DiscoveryCandidate(url.Trim(), "environment-mongo-urls", DiscoveryCandidatePriority.Environment));
    }

    /// <summary>MongoDB-specific connection string parameter application — delegates to the shared
    /// <see cref="MongoConnectionString.ApplyParameters"/> (replica-set-safe string manipulation),
    /// preserving the dictionary-presence semantics: auth is applied only when BOTH username and password
    /// keys are present; the database is applied when its key is present.</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            string? username = null;
            string? password = null;
            if (parameters.TryGetValue("username", out var u) &&
                parameters.TryGetValue("password", out var p))
            {
                username = u?.ToString();
                password = p?.ToString();
            }

            var database = parameters.TryGetValue("database", out var db) ? db?.ToString() : null;

            return MongoConnectionString.ApplyParameters(baseUrl, database, username, password);
        }
        catch (Exception ex)
        {
            ReportNormalizationFailure(baseUrl, ex);
            return baseUrl;
        }
    }

    /// <summary>MongoDB adapter handles Aspire service discovery for MongoDB</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific MongoDB service discovery
        return _configuration["services:mongodb:default:0"] ??
               _configuration["services:mongo:default:0"];
    }
}
