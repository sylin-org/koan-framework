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
        try
        {
            var settings = MongoClientSettings.FromConnectionString(serviceUrl);
            settings.ServerSelectionTimeout = context.HealthCheckTimeout;

            var client = new MongoClient(settings);
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken);

            _logger.LogDebug("MongoDB health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("MongoDB health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>MongoDB adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check MongoDB-specific configuration paths
        return _configuration.GetConnectionString("MongoDB") ??
               _configuration["Koan:Data:Mongo:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>MongoDB-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var mongoUrls = Environment.GetEnvironmentVariable("MONGO_URLS") ??
                       Environment.GetEnvironmentVariable("MONGODB_URLS");

        if (string.IsNullOrWhiteSpace(mongoUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return mongoUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(url => new DiscoveryCandidate(url.Trim(), "environment-mongo-urls", 0));
    }

    /// <summary>MongoDB-specific connection string parameter application.
    /// Uses string manipulation rather than System.Uri to support replica set
    /// connection strings with comma-separated hosts.</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            // Format: mongodb[+srv]://[existing-auth@]hosts[/db][?options]
            var schemeEnd = baseUrl.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0) return baseUrl;

            var scheme = baseUrl[..(schemeEnd + 3)];
            var rest = baseUrl[(schemeEnd + 3)..];

            // Detect existing auth (@ must appear before any / or ?)
            var atIndex = rest.IndexOf('@');
            var slashIndex = rest.IndexOf('/');
            if (atIndex >= 0 && (slashIndex < 0 || atIndex < slashIndex))
            {
                rest = rest[(atIndex + 1)..];
            }

            // Split hosts from path+query
            string hosts;
            string trailing = "";
            var pathStart = rest.IndexOf('/');
            if (pathStart >= 0)
            {
                hosts = rest[..pathStart];
                trailing = rest[pathStart..];
            }
            else
            {
                var queryStart = rest.IndexOf('?');
                hosts = queryStart >= 0 ? rest[..queryStart] : rest;
                trailing = queryStart >= 0 ? rest[queryStart..] : "";
            }

            var auth = "";
            if (parameters.TryGetValue("username", out var username) &&
                parameters.TryGetValue("password", out var password))
            {
                auth = $"{username}:{password}@";
            }

            if (parameters.TryGetValue("database", out var db))
            {
                // Replace existing path with requested database, preserve query
                var qIdx = trailing.IndexOf('?');
                var query = qIdx >= 0 ? trailing[qIdx..] : "";
                trailing = $"/{db}{query}";
            }

            return $"{scheme}{auth}{hosts}{trailing}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to apply MongoDB parameters to {BaseUrl}: {Error}", baseUrl, ex.Message);
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
