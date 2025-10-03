using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.SqlServer.Discovery;

/// <summary>
/// SQL Server autonomous discovery adapter.
/// Contains ALL SQL Server-specific knowledge - core orchestration knows nothing about SQL Server.
/// Reads own KoanServiceAttribute and handles SQL Server-specific health checks.
/// </summary>
internal sealed class SqlServerDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "mssql";
    public override string[] Aliases => new[] { "sqlserver", "microsoft.sqlserver" };

    public SqlServerDiscoveryAdapter(IConfiguration configuration, ILogger<SqlServerDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>SQL Server adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(SqlServerAdapterFactory);

    /// <summary>SQL Server-specific health validation using connection test</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Build connection string from discovered URL and context parameters
            var connectionString = BuildSqlServerConnectionString(serviceUrl, context.Parameters);

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Simple query to test connectivity
            using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogDebug("SQL Server health check passed for {Url}", serviceUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("SQL Server health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>SQL Server adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check SQL Server-specific configuration paths
        return _configuration.GetConnectionString("SqlServer") ??
               _configuration.GetConnectionString("MSSQL") ??
               _configuration["Koan:Data:SqlServer:ConnectionString"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>SQL Server-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add SQL Server-specific candidates from environment variables (highest priority)
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
                _logger.LogDebug("SQL Server adapter: Added container candidate {ContainerUrl} (in container environment)", containerUrl);
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                _logger.LogDebug("SQL Server adapter: Added local fallback {LocalUrl}", localhostUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("SQL Server adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
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
                _logger.LogDebug("SQL Server adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        // Apply SQL Server-specific connection parameters if provided
        if (context.Parameters != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i].Url))
                {
                    candidates[i] = candidates[i] with
                    {
                        Url = BuildSqlServerConnectionString(candidates[i].Url, context.Parameters)
                    };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>SQL Server-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var sqlServerUrls = Environment.GetEnvironmentVariable("SQLSERVER_URLS") ??
                           Environment.GetEnvironmentVariable("MSSQL_URLS");

        if (string.IsNullOrWhiteSpace(sqlServerUrls))
            return Enumerable.Empty<DiscoveryCandidate>();

        return sqlServerUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(url => new DiscoveryCandidate(url.Trim(), "environment-sqlserver-urls", 0));
    }

    /// <summary>SQL Server-specific connection string construction</summary>
    private string BuildSqlServerConnectionString(string baseUrl, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Handle mssql:// URL format
            if (baseUrl.StartsWith("mssql://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(baseUrl);
                var server = $"{uri.Host},{(uri.Port == -1 ? 1433 : uri.Port)}";
                var database = "Koan";
                var userId = "sa";
                var password = "Your_password123";

                // Apply parameters if provided
                if (parameters != null)
                {
                    if (parameters.TryGetValue("database", out var db))
                        database = db.ToString() ?? database;
                    if (parameters.TryGetValue("userId", out var user))
                        userId = user.ToString() ?? userId;
                    if (parameters.TryGetValue("password", out var pass))
                        password = pass.ToString() ?? password;
                }

                return $"Server={server};Database={database};User Id={userId};Password={password};TrustServerCertificate=True";
            }

            // If it's already a connection string format, return as-is
            return baseUrl;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to build SQL Server connection string from {BaseUrl}: {Error}", baseUrl, ex.Message);
            return baseUrl; // Return original URL if parsing fails
        }
    }

    /// <summary>SQL Server adapter handles Aspire service discovery for SQL Server</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific SQL Server service discovery
        return _configuration["services:mssql:default:0"] ??
               _configuration["services:sqlserver:default:0"];
    }
}
