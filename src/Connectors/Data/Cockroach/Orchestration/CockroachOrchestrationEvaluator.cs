using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Connector.Cockroach.Orchestration;

/// <summary>
/// CockroachDB-specific orchestration evaluator that determines if CockroachDB containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class CockroachOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public CockroachOrchestrationEvaluator(ILogger<CockroachOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => "cockroach";
    public override int StartupPriority => 100; // Infrastructure databases register early

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // CockroachDB is enabled when:
        // 1. Package is referenced (Reference = Intent principle)
        // 2. OR when explicitly configured
        // Since this evaluator exists, the package is referenced, so enable for auto-discovery
        return true;
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit CockroachDB connection configuration
        return !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.AltConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionStringsCockroach]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:cockroach"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:cockroachdb"]);
    }

    protected override int GetDefaultPort()
    {
        return 26257; // Standard CockroachDB port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var pgHost = Environment.GetEnvironmentVariable("PGHOST");
        var pgPort = Environment.GetEnvironmentVariable("PGPORT");
        if (!string.IsNullOrWhiteSpace(pgHost))
        {
            var port = !string.IsNullOrWhiteSpace(pgPort) ? pgPort : "26257";
            candidates.Add($"{pgHost}:{port}");
        }

        var cockroachUrl = Environment.GetEnvironmentVariable("POSTGRES_URL");
        if (!string.IsNullOrWhiteSpace(cockroachUrl))
        {
            var host = ExtractHostFromConnectionString(cockroachUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanCockroachUrl = Environment.GetEnvironmentVariable("Koan_POSTGRES_URL");
        if (!string.IsNullOrWhiteSpace(koanCockroachUrl))
        {
            var host = ExtractHostFromConnectionString(koanCockroachUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            ReportCredentialValidation("start", ("host", hostResult.HostEndpoint));

            // Get configured connection settings
            var connectionString = BuildCockroachConnectionString(hostResult.HostEndpoint!, configuration);

            // Try to connect with the configured credentials
            var isValid = await TryCockroachConnection(connectionString);

            ReportCredentialValidation(isValid ? "accepted" : "rejected");
            return isValid;
        }
        catch (Exception ex)
        {
            ReportCredentialValidation("failed", ("error", ex));
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptor(IConfiguration configuration, OrchestrationContext context)
    {
        // Parse connection string to extract database name, username, and password
        var options = new CockroachOptions();
        new CockroachOptionsConfigurator(configuration).Configure(options);
        var connectionParts = ParseConnectionString(options.ConnectionString);

        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "cockroach",
            ["POSTGRES_DB"] = connectionParts.Database ?? "Koan",
            ["POSTGRES_USER"] = connectionParts.Username ?? "root"
        };

        // Only set password if one is provided and not empty
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            environment["POSTGRES_PASSWORD"] = connectionParts.Password;
        }

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "cockroachdb/cockroach:v23.2.4",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "pg_isready -U root",
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-cockroach-{context.SessionId}:/var/lib/cockroachdb/data"
            }
        });
    }

    private static string BuildCockroachConnectionString(string hostPort, IConfiguration configuration)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "26257";

        // Get configured connection settings
        var options = new CockroachOptions();
        new CockroachOptionsConfigurator(configuration).Configure(options);
        var connectionParts = ParseConnectionString(options.ConnectionString);

        // Build connection string with host override
        var username = connectionParts.Username ?? "root";
        var password = connectionParts.Password ?? "";
        var database = connectionParts.Database ?? "Koan";

        return $"Host={host};Port={port};Database={database};Username={username};Password={password};";
    }

    private static string? ExtractHostFromConnectionString(string connectionString)
    {
        try
        {
            // Handle cockroachdb:// URL format
            if (connectionString.StartsWith("cockroachdb://", StringComparison.OrdinalIgnoreCase) ||
                connectionString.StartsWith("cockroach://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Handle standard CockroachDB connection string format
            var connectionParts = ParseConnectionString(connectionString);
            return $"localhost:{connectionParts.Port}"; // Default to localhost for host extraction
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryCockroachConnection(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // Verify we can actually do basic operations
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync();

            return result?.ToString() == "1";
        }
        catch
        {
            return false;
        }
    }

    private static (string? Database, string? Username, string? Password, int Port) ParseConnectionString(string connectionString)
    {
        // Simple connection string parsing for CockroachDB
        // Format: "Host=localhost;Port=26257;Database=Koan;Username=cockroach;Password=cockroach"
        var parts = connectionString.Split(';');
        string? database = null;
        string? username = null;
        string? password = null;
        int port = 26257;

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                var key = keyValue[0].Trim().ToLowerInvariant();
                var value = keyValue[1].Trim();

                switch (key)
                {
                    case "database":
                        database = value;
                        break;
                    case "username":
                    case "user id":
                    case "userid":
                    case "uid":
                        username = value;
                        break;
                    case "password":
                    case "pwd":
                        password = value;
                        break;
                    case "port":
                        if (int.TryParse(value, out var parsedPort))
                            port = parsedPort;
                        break;
                }
            }
        }

        return (database, username, password, port);
    }
}
