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

    /// <summary>Couchbase-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var couchbaseUrls = Environment.GetEnvironmentVariable("COUCHBASE_URLS") ??
                           Environment.GetEnvironmentVariable("CB_URLS");

        if (string.IsNullOrWhiteSpace(couchbaseUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return couchbaseUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(url => new DiscoveryCandidate(url.Trim(), "environment-couchbase-urls", 0));
    }

    /// <summary>Couchbase-specific connection string normalization</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return NormalizeCouchbaseConnectionString(baseUrl);
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
