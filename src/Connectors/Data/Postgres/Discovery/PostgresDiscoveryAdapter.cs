using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Postgres.Discovery;

/// <summary>
/// PostgreSQL autonomous discovery adapter.
/// Contains ALL PostgreSQL-specific knowledge - core orchestration knows nothing about PostgreSQL.
/// Reads own KoanServiceAttribute and handles PostgreSQL-specific health checks.
/// </summary>
internal sealed class PostgresDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "postgres";
    public override string[] Aliases => new[] { "postgresql", "npgsql" };

    public PostgresDiscoveryAdapter(IConfiguration configuration, ILogger<PostgresDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>PostgreSQL adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(PostgresAdapterFactory);

    /// <summary>PostgreSQL-specific health validation using connection test</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Build connection string from discovered URL and context parameters
            var connectionString = BuildPostgresConnectionString(serviceUrl, context.Parameters);

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Simple query to test connectivity
            using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogDebug("PostgreSQL health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("PostgreSQL health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>PostgreSQL adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check PostgreSQL-specific configuration paths
        return _configuration.GetConnectionString("PostgreSQL") ??
               _configuration.GetConnectionString("Postgres") ??
               _configuration["Koan:Data:Postgres:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>PostgreSQL-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var postgresUrls = Environment.GetEnvironmentVariable("POSTGRES_URLS") ??
                          Environment.GetEnvironmentVariable("POSTGRESQL_URLS");

        if (string.IsNullOrWhiteSpace(postgresUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return postgresUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(url => new DiscoveryCandidate(url.Trim(), "environment-postgres-urls", 0));
    }

    /// <summary>PostgreSQL-specific connection string construction</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return BuildPostgresConnectionString(baseUrl, parameters);
    }

    /// <summary>Build PostgreSQL connection string from URL and optional parameters</summary>
    private string BuildPostgresConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle postgres:// URL format
            if (baseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(baseUrl);
                var host = uri.Host;
                var port = uri.Port == -1 ? 5432 : uri.Port;
                var database = string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')) ? "Koan" : uri.PathAndQuery.Trim('/');
                var username = "postgres";
                var password = "postgres";

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
            _logger.LogDebug("Failed to build PostgreSQL connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>PostgreSQL adapter handles Aspire service discovery for PostgreSQL</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific PostgreSQL service discovery
        return _configuration["services:postgresql:default:0"] ??
               _configuration["services:postgres:default:0"];
    }
}
