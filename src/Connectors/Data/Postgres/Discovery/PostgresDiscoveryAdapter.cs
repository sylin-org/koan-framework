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

    /// <summary>PostgreSQL-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add PostgreSQL-specific candidates from environment variables (highest priority)
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
                _logger.LogDebug("PostgreSQL adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("PostgreSQL adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("PostgreSQL adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
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
                _logger.LogDebug("PostgreSQL adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply PostgreSQL-specific connection parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = BuildPostgresConnectionString(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>PostgreSQL-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var postgresUrls = Environment.GetEnvironmentVariable("POSTGRES_URLS") ??
                          Environment.GetEnvironmentVariable("POSTGRESQL_URLS");

        if (string.IsNullOrWhiteSpace(postgresUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return postgresUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(url => new DiscoveryCandidate(url.Trim(), "environment-postgres-urls", 0));
    }

    /// <summary>PostgreSQL-specific connection string construction</summary>
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
