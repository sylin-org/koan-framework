using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Postgres.Orchestration;

/// <summary>
/// PostgreSQL-specific orchestration evaluator that determines if PostgreSQL containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class PostgresOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public PostgresOrchestrationEvaluator(ILogger<PostgresOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => "postgres";
    public override int StartupPriority => 100; // Infrastructure databases register early

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // PostgreSQL is enabled when:
        // 1. Package is referenced (Reference = Intent principle)
        // 2. OR when explicitly configured
        // Since this evaluator exists, the package is referenced, so enable for auto-discovery
        return true;
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit PostgreSQL connection configuration
        return !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.AltConnectionString]) ||
               !string.IsNullOrEmpty(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:postgres"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:postgresql"]);
    }

    protected override int GetDefaultPort()
    {
        return 5432; // Standard PostgreSQL port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var pgHost = Environment.GetEnvironmentVariable("PGHOST");
        var pgPort = Environment.GetEnvironmentVariable("PGPORT");
        if (!string.IsNullOrWhiteSpace(pgHost))
        {
            var port = !string.IsNullOrWhiteSpace(pgPort) ? pgPort : "5432";
            candidates.Add($"{pgHost}:{port}");
        }

        var postgresUrl = Environment.GetEnvironmentVariable("POSTGRES_URL");
        if (!string.IsNullOrWhiteSpace(postgresUrl))
        {
            var host = ExtractHostFromConnectionString(postgresUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanPostgresUrl = Environment.GetEnvironmentVariable("Koan_POSTGRES_URL");
        if (!string.IsNullOrWhiteSpace(koanPostgresUrl))
        {
            var host = ExtractHostFromConnectionString(koanPostgresUrl);
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
            Logger?.LogDebug("[PostgreSQL] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Get configured connection settings
            var connectionString = BuildPostgresConnectionString(hostResult.HostEndpoint!, configuration);

            // Try to connect with the configured credentials
            var isValid = await TryPostgresConnection(connectionString);

            Logger?.LogDebug("[PostgreSQL] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[PostgreSQL] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Parse connection string to extract database name, username, and password
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(configuration).Configure(options);
        var connectionParts = ParseConnectionString(options.ConnectionString);

        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "postgres",
            ["POSTGRES_DB"] = connectionParts.Database ?? "Koan",
            ["POSTGRES_USER"] = connectionParts.Username ?? "postgres"
        };

        // Only set password if one is provided and not empty
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            environment["POSTGRES_PASSWORD"] = connectionParts.Password;
        }

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "postgres:16-alpine",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "pg_isready -U postgres",
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-postgres-{context.SessionId}:/var/lib/postgresql/data"
            }
        });
    }

    private static string BuildPostgresConnectionString(string hostPort, IConfiguration configuration)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "5432";

        // Get configured connection settings
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(configuration).Configure(options);
        var connectionParts = ParseConnectionString(options.ConnectionString);

        // Build connection string with host override
        var username = connectionParts.Username ?? "postgres";
        var password = connectionParts.Password ?? "postgres";
        var database = connectionParts.Database ?? "Koan";

        return $"Host={host};Port={port};Database={database};Username={username};Password={password};";
    }

    private static string? ExtractHostFromConnectionString(string connectionString)
    {
        try
        {
            // Handle postgresql:// URL format
            if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
                connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Handle standard PostgreSQL connection string format
            var connectionParts = ParseConnectionString(connectionString);
            return $"localhost:{connectionParts.Port}"; // Default to localhost for host extraction
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryPostgresConnection(string connectionString)
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
        // Simple connection string parsing for PostgreSQL
        // Format: "Host=localhost;Port=5432;Database=Koan;Username=postgres;Password=postgres"
        var parts = connectionString.Split(';');
        string? database = null;
        string? username = null;
        string? password = null;
        int port = 5432;

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