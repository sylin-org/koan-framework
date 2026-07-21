using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Services;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Data.Connector.Sqlite.Discovery;

/// <summary>
/// SQLite autonomous discovery adapter.
/// Contains ALL SQLite-specific knowledge - core orchestration knows nothing about SQLite.
/// Handles SQLite-specific file path resolution and health checks.
/// </summary>
internal sealed class SqliteDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "sqlite";
    public override string[] Aliases => new[] { "db", "file" };

    public SqliteDiscoveryAdapter(IConfiguration configuration, ILogger<SqliteDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>SQLite adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(SqliteAdapterFactory);

    /// <summary>SQLite-specific health validation using file access test</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            // SQLite uses connection strings like "Data Source=path/to/file.db"
            var connectionString = NormalizeSqliteConnectionString(serviceUrl);

            var probeConnection = new SqliteConnectionStringBuilder(connectionString)
            {
                Pooling = false,
            }.ConnectionString;
            using var connection = new SqliteConnection(probeConnection);
            await connection.OpenAsync(cancellationToken);

            // Simple query to test connectivity
            using var command = new SqliteCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            KoanLog.ConfigDebug(_logger, LogActions.Health, LogOutcomes.Success,
                ("connection", Redaction.DeIdentify(connectionString)));
            return true;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigDebug(_logger, LogActions.Health, LogOutcomes.Failure,
                ("connection", Redaction.DeIdentify(serviceUrl)),
                ("error", Redaction.DeIdentify(ex.Message)));
            return false;
        }
    }

    /// <summary>SQLite adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Keep discovery's explicit candidate in lockstep with the options/boot fallback order. A higher-priority
        // literal "auto" deliberately delegates to discovery instead of falling through to a lower concrete key.
        var configured = Infrastructure.SqliteConnectionConfiguration.ReadProviderFallback(_configuration);
        return Infrastructure.SqliteConnectionConfiguration.IsAuto(configured) ? null : configured;
    }

    protected override IEnumerable<DiscoveryCandidate> BuildRuntimeCandidates(KoanServiceAttribute attribute)
    {
        var candidates = base.BuildRuntimeCandidates(attribute).ToList();
        candidates.Add(new DiscoveryCandidate(
            BuildSqliteConnectionString(Infrastructure.Constants.Configuration.DataFallback.DefaultSource),
            "embedded-default",
            DiscoveryCandidatePriority.LoopbackFallback));
        return candidates;
    }

    /// <summary>SQLite-specific environment variable handling</summary>
    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var sqliteFiles = Environment.GetEnvironmentVariable("SQLITE_FILES") ??
                         Environment.GetEnvironmentVariable("SQLITE_PATH");

        if (string.IsNullOrWhiteSpace(sqliteFiles))
            return Enumerable.Empty<DiscoveryCandidate>();

        return sqliteFiles.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(path => new DiscoveryCandidate($"Data Source={path.Trim()}", "environment-sqlite-files", DiscoveryCandidatePriority.Environment));
    }

    /// <summary>SQLite-specific connection string normalization</summary>
    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return NormalizeSqliteConnectionString(baseUrl);
    }

    /// <summary>SQLite-specific connection string normalization</summary>
    private string NormalizeSqliteConnectionString(string value)
    {
        try
        {
            var trimmed = value?.Trim() ?? "";
            if (string.IsNullOrEmpty(trimmed)) return BuildSqliteConnectionString(Infrastructure.Constants.Configuration.DataFallback.DefaultSource);

            // If already properly formatted, return as-is
            if (trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            // If it's just a file path, add the Data Source prefix
            return $"Data Source={trimmed}";
        }
        catch (Exception ex)
        {
            KoanLog.ConfigDebug(_logger, LogActions.Normalize, LogOutcomes.Failure,
                ("value", Redaction.DeIdentify(value)),
                ("error", Redaction.DeIdentify(ex.Message)));
            return value; // Return original value if normalization fails
        }
    }

    private static string BuildSqliteConnectionString(string filePath)
        => $"Data Source={filePath}";

    private static class LogActions
    {
        public const string Health = "sqlite.health";
        public const string Candidate = "sqlite.discovery.candidate";
        public const string Normalize = "sqlite.discovery.normalize";
    }

    private static class LogOutcomes
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Add = "add";
    }

    /// <summary>SQLite adapter handles Aspire service discovery (not typically used for file-based databases)</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // SQLite is file-based so Aspire discovery is not typically relevant
        return _configuration["services:sqlite:default:0"];
    }
}
