using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using MongoConstants = Koan.Data.Connector.Mongo.Infrastructure.Constants;

namespace Koan.Data.Connector.Mongo.Discovery;

/// <summary>
/// MongoDB autonomous discovery adapter.
/// Contains ALL MongoDB-specific knowledge - core orchestration knows nothing about MongoDB.
/// Reads own KoanServiceAttribute and handles MongoDB-specific health checks.
/// </summary>
internal sealed class MongoDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => MongoConstants.Discovery.ServiceName;
    public override string[] Aliases => [MongoConstants.Provider.Alias];

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
        return _configuration.GetConnectionString(MongoConstants.Provider.ConfigurationName) ??
               _configuration[MongoConstants.Configuration.ConnectionString] ??
               _configuration[MongoConstants.Configuration.DefaultSourceConnectionString];
    }

    /// <summary>MongoDB-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var mongoUrls = Environment.GetEnvironmentVariable(MongoConstants.Discovery.MongoUrls) ??
                       Environment.GetEnvironmentVariable(MongoConstants.Discovery.MongoDbUrls);

        if (string.IsNullOrWhiteSpace(mongoUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return mongoUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(url => new DiscoveryCandidate(url.Trim(), "environment-mongo-urls", DiscoveryCandidatePriority.Environment));
    }

    /// <summary>MongoDB adapter handles Aspire service discovery for MongoDB</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific MongoDB service discovery
        return _configuration["services:mongodb:default:0"] ??
               _configuration["services:mongo:default:0"];
    }
}
