using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Redis.Discovery;

/// <summary>
/// Redis autonomous discovery adapter.
/// Contains ALL Redis-specific knowledge - core orchestration knows nothing about Redis.
/// Reads own KoanServiceAttribute and handles Redis-specific health checks.
/// </summary>
public sealed class RedisDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "redis";
    public override string[] Aliases => new[] { "cache" };

    public RedisDiscoveryAdapter(IConfiguration configuration, ILogger<RedisDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>Redis adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(RedisAdapterFactory);

    /// <summary>Redis-specific health validation using Redis ping command</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            var options = ConfigurationOptions.Parse(serviceUrl);
            options.ConnectTimeout = (int)context.HealthCheckTimeout.TotalMilliseconds;
            options.SyncTimeout = (int)context.HealthCheckTimeout.TotalMilliseconds;

            using var muxer = ConnectionMultiplexer.Connect(options);
            var database = muxer.GetDatabase();
            await database.PingAsync();

            _logger.LogDebug("Redis health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Redis health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Redis adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Redis-specific configuration paths
        return _configuration.GetConnectionString("Redis") ??
               _configuration[Infrastructure.Constants.Discovery.EnvRedisUrl] ??
               _configuration[Infrastructure.Constants.Discovery.EnvRedisConnectionString] ??
               _configuration["Koan:Data:Redis:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>Redis-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add Redis-specific candidates from environment variables (highest priority)
        candidates.AddRange(GetEnvironmentCandidates());

        // Add explicit configuration candidates
        var explicitConfig = ReadExplicitConfiguration();
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(explicitConfig, "explicit-config", 1));
        }

        // Container vs Local detection logic
        if (KoanEnv.InContainer)
        {
            // In container: Try container instance first, then local fallback
            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(containerUrl, "container-instance", 2));
                _logger.LogDebug("Redis adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("Redis adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("Redis adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
            }
        }

        // Special handling for Aspire
        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = ReadAspireServiceDiscovery();
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                // Aspire takes priority over container/local discovery
                candidates.Insert(0, new DiscoveryCandidate(aspireUrl, "aspire-discovery", 1));
                _logger.LogDebug("Redis adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply Redis-specific connection parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = ApplyRedisConnectionParameters(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>Redis-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var redisUrls = Environment.GetEnvironmentVariable("REDIS_URLS") ??
                       Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRINGS");

        if (string.IsNullOrWhiteSpace(redisUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return redisUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(url => new DiscoveryCandidate(url.Trim(), "environment-redis-urls", 0));
    }

    /// <summary>Redis-specific connection string parameter application</summary>
    private string ApplyRedisConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            // Handle redis:// URL format
            if (baseUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(baseUrl);
                var auth = "";
                var options = "";

                // Apply Redis-specific authentication parameters
                if (parameters.TryGetValue("password", out var password))
                {
                    auth = $":{password}@";
                }

                // Apply Redis-specific database parameter
                if (parameters.TryGetValue("database", out var db))
                {
                    options = $"/{db}";
                }

                return $"{uri.Scheme}://{auth}{uri.Host}:{uri.Port}{options}";
            }

            // Handle host:port format - convert to Redis connection string format
            var connectionString = baseUrl;
            if (parameters.TryGetValue("password", out var pwd))
            {
                connectionString += $",password={pwd}";
            }
            if (parameters.TryGetValue("database", out var database))
            {
                connectionString += $",defaultDatabase={database}";
            }

            return connectionString;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to apply Redis parameters to {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parameter application fails
        }
    }

    /// <summary>Redis adapter handles Aspire service discovery for Redis</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Redis service discovery
        return _configuration["services:redis:default:0"];
    }
}
