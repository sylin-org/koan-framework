using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Couchbase;
using Couchbase.Core.Configuration.Server;
using Couchbase.KeyValue;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Couchbase.Discovery;

/// <summary>
/// Couchbase autonomous discovery adapter.
/// Contains ALL Couchbase-specific knowledge - core orchestration knows nothing about Couchbase.
/// Reads own KoanServiceAttribute and handles Couchbase-specific health checks.
/// </summary>
internal sealed class CouchbaseDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "couchbase";
    public override string[] Aliases => new[] { "cb", "nosql" };

    public CouchbaseDiscoveryAdapter(IConfiguration configuration, ILogger<CouchbaseDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>Couchbase adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(CouchbaseAdapterFactory);

    /// <summary>Couchbase-specific health validation using cluster test</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = NormalizeCouchbaseConnectionString(serviceUrl);

            // Configure cluster options for health check
            var options = new ClusterOptions()
            {
                ConnectionString = connectionString
            };

            // Apply authentication if provided in context
            if (context.Parameters != null)
            {
                if (context.Parameters.TryGetValue("username", out var username) &&
                    context.Parameters.TryGetValue("password", out var password))
                {
                    options.UserName = username.ToString();
                    options.Password = password.ToString();
                }
            }

            // Set default credentials if none provided
            if (string.IsNullOrEmpty(options.UserName))
            {
                options.UserName = "Administrator";
                options.Password = "password";
            }

            using var cluster = await Cluster.ConnectAsync(options);

            // Simple health check - try to get bucket info
            var bucketName = context.Parameters?.TryGetValue("bucket", out var bucket) == true
                ? bucket.ToString() ?? "Koan"
                : "Koan";

            try
            {
                var testBucket = await cluster.BucketAsync(bucketName);
                await testBucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(5));

                _logger.LogDebug("Couchbase health check passed for {Url}", serviceUrl);
                return true;
            }
            catch
            {
                // If bucket doesn't exist or isn't ready, just check cluster connection
                var buckets = await cluster.Buckets.GetAllBucketsAsync();
                _logger.LogDebug("Couchbase cluster health check passed for {Url} (bucket not accessible)", serviceUrl);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Couchbase health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Couchbase adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Couchbase-specific configuration paths
        return _configuration.GetConnectionString("Couchbase") ??
               _configuration["Koan:Data:Couchbase:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>Couchbase-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add Couchbase-specific candidates from environment variables (highest priority)
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
                _logger.LogDebug("Couchbase adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("Couchbase adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("Couchbase adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
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
                _logger.LogDebug("Couchbase adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply Couchbase-specific normalization
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[i].Url))
            {
                candidates[i] = candidates[i] with
                {
                    Url = NormalizeCouchbaseConnectionString(candidates[i].Url)
                };
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>Couchbase-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var couchbaseUrls = Environment.GetEnvironmentVariable("COUCHBASE_URLS") ??
                           Environment.GetEnvironmentVariable("CB_URLS");

        if (string.IsNullOrWhiteSpace(couchbaseUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return couchbaseUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(url => new DiscoveryCandidate(url.Trim(), "environment-couchbase-urls", 0));
    }

    /// <summary>Couchbase-specific connection string normalization</summary>
    private string NormalizeCouchbaseConnectionString(string value)
    {
        try
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed)) return "couchbase://localhost";

            // If already properly formatted, return as-is
            if (trimmed.StartsWith("couchbase://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("couchbases://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            // Handle HTTP URLs - convert to couchbase protocol
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Replace("http://", "couchbase://", StringComparison.OrdinalIgnoreCase);
            }
            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Replace("https://", "couchbases://", StringComparison.OrdinalIgnoreCase);
            }

            // If it's just a hostname or IP, add the couchbase protocol
            return $"couchbase://{trimmed}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to normalize Couchbase connection string from {Value}: {Error}", value, ex.Message);
            return value; // Return original value if normalization fails
        }
    }

    /// <summary>Couchbase adapter handles Aspire service discovery for Couchbase</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Couchbase service discovery
        return _configuration["services:couchbase:default:0"] ??
               _configuration["services:cb:default:0"];
    }
}
