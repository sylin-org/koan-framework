using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Cockroach.Discovery;

/// <summary>
/// CockroachDB autonomous discovery adapter.
/// Contains ALL CockroachDB-specific knowledge - core orchestration knows nothing about CockroachDB.
/// Reads own KoanServiceAttribute and handles CockroachDB-specific health checks.
/// </summary>
internal sealed class CockroachDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "cockroach";
    // Only cockroach/cockroachdb — the `npgsql` alias stays with the Postgres discovery adapter so an app that
    // references both connectors disambiguates each engine.
    public override string[] Aliases => new[] { "cockroachdb" };

    public CockroachDiscoveryAdapter(IConfiguration configuration, ILogger<CockroachDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>CockroachDB adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(CockroachAdapterFactory);

    /// <summary>CockroachDB-specific health validation using connection test</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Build connection string from discovered URL and context parameters
            var connectionString = BuildCockroachConnectionString(serviceUrl, context.Parameters);

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Simple query to test connectivity
            using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogDebug("CockroachDB health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("CockroachDB health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>CockroachDB adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check CockroachDB-specific configuration paths
        return _configuration.GetConnectionString("CockroachDB") ??
               _configuration.GetConnectionString("Cockroach") ??
               _configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString] ??
               _configuration[Infrastructure.Constants.Configuration.DataFallback.ConnectionString];
    }

    /// <summary>CockroachDB-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var cockroachUrls = Environment.GetEnvironmentVariable("POSTGRES_URLS") ??
                          Environment.GetEnvironmentVariable("POSTGRESQL_URLS");

        if (string.IsNullOrWhiteSpace(cockroachUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return cockroachUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(url => new DiscoveryCandidate(url.Trim(), "environment-cockroach-urls", DiscoveryCandidatePriority.Environment));
    }

    /// <summary>CockroachDB-specific connection string construction</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return BuildCockroachConnectionString(baseUrl, parameters);
    }

    /// <summary>Build CockroachDB connection string from URL and optional parameters</summary>
    private string BuildCockroachConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle cockroach:// URL format
            if (baseUrl.StartsWith("cockroach://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(baseUrl);
                var host = uri.Host;
                var port = uri.Port == -1 ? 26257 : uri.Port;
                var database = string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')) ? "Koan" : uri.PathAndQuery.Trim('/');
                var username = "root";   // CockroachDB --insecure default user (no password)
                var password = "";

                // Apply parameters if provided
                if (parameters != null)
                {
                    if (parameters.TryGetValue("database", out var db))
                        database = db.ToString() ?? database;
                    if (parameters.TryGetValue("username", out var user))
                        username = user.ToString() ?? username;
                    if (parameters.TryGetValue("password", out var pass))
                        password = pass.ToString() ?? password;
                }

                return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
            }

            // If it's already a connection string format, return as-is
            return baseUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to build CockroachDB connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>CockroachDB adapter handles Aspire service discovery for CockroachDB</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific CockroachDB service discovery
        return _configuration["services:cockroachdb:default:0"] ??
               _configuration["services:cockroach:default:0"];
    }
}
