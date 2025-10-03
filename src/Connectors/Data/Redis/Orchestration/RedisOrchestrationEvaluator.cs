using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Data.Connector.Redis.Orchestration;

/// <summary>
/// Redis-specific orchestration evaluator that determines if Redis containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class RedisOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public RedisOrchestrationEvaluator(ILogger<RedisOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => "redis";
    public override int StartupPriority => 300; // After data services, cache layer

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // Redis is typically enabled when data adapters reference it or when explicitly configured
        // For now, be conservative and only enable if explicitly configured
        return HasExplicitConfiguration(configuration);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit Redis connection configuration
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        return !string.IsNullOrEmpty(options.ConnectionString) ||
               !string.IsNullOrEmpty(configuration["Redis:ConnectionString"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:Redis"]) ||
               !string.IsNullOrEmpty(configuration["ConnectionStrings:redis"]);
    }

    protected override int GetDefaultPort()
    {
        return 6379; // Standard Redis port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
        if (!string.IsNullOrWhiteSpace(redisUrl))
        {
            var host = ExtractHostFromConnectionString(redisUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanRedisUrl = Environment.GetEnvironmentVariable("Koan_REDIS_URL");
        if (!string.IsNullOrWhiteSpace(koanRedisUrl))
        {
            var host = ExtractHostFromConnectionString(koanRedisUrl);
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
            Logger?.LogDebug("[Redis] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Get configured credentials and database
            var options = new RedisOptions();
            new RedisOptionsConfigurator(configuration).Configure(options);

            // Build connection string for validation
            var connectionString = BuildRedisConnectionString(hostResult.HostEndpoint!, options);

            // Try to connect with the configured settings
            var isValid = await Task.Run(() => TryRedisConnection(connectionString));

            Logger?.LogDebug("[Redis] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[Redis] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Get configuration values
        var options = new RedisOptions();
        new RedisOptionsConfigurator(configuration).Configure(options);

        // Parse connection string to extract password if provided
        var connectionParts = ParseRedisConnectionString(options.ConnectionString);

        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "redis"
        };

        // Set password if one is provided
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            environment["REDIS_PASSWORD"] = connectionParts.Password;
        }

        // Set default database if not 0
        if (options.Database != 0)
        {
            environment["REDIS_DEFAULT_DB"] = options.Database.ToString();
        }

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "redis:7-alpine",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "redis-cli ping",
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-redis-{context.SessionId}:/data"
            }
        });
    }

    private static string BuildRedisConnectionString(string hostPort, RedisOptions options)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "6379";

        // Build basic connection string
        var connectionString = $"{host}:{port}";

        // Add database if specified
        if (options.Database != 0)
        {
            connectionString += $",defaultDatabase={options.Database}";
        }

        // Add password if configured in options
        var connectionParts = ParseRedisConnectionString(options.ConnectionString);
        if (!string.IsNullOrEmpty(connectionParts.Password))
        {
            connectionString += $",password={connectionParts.Password}";
        }

        return connectionString;
    }

    private static string? ExtractHostFromConnectionString(string connectionString)
    {
        try
        {
            // Handle redis:// URL format
            if (connectionString.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Handle comma-separated options format - extract first part which is host:port
            var parts = connectionString.Split(',');
            var hostPort = parts[0].Trim();

            // Check if it's just host without port
            if (!hostPort.Contains(':'))
            {
                return $"{hostPort}:6379";
            }

            return hostPort;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryRedisConnection(string connectionString)
    {
        try
        {
            var multiplexer = ConnectionMultiplexer.Connect(connectionString);
            using (multiplexer)
            {
                var database = multiplexer.GetDatabase();

                // Verify we can actually do basic operations
                var testKey = "Koan:test:" + Guid.NewGuid().ToString("N")[..8];
                database.StringSet(testKey, "test");
                var result = database.StringGet(testKey);
                database.KeyDelete(testKey);

                return result == "test";
            }
        }
        catch
        {
            return false;
        }
    }

    private static (int Port, string? Password) ParseRedisConnectionString(string? connectionString)
    {
        // Default values
        int port = 6379;
        string? password = null;

        if (string.IsNullOrEmpty(connectionString))
        {
            return (port, password);
        }

        try
        {
            // Handle redis:// URL format
            if (connectionString.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                port = uri.Port != -1 ? uri.Port : 6379;

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var userInfo = uri.UserInfo.Split(':');
                    if (userInfo.Length > 1)
                        password = userInfo[1];
                }

                return (port, password);
            }

            // Handle comma-separated options format
            var parts = connectionString.Split(',');
            var hostPort = parts[0].Trim();

            // Extract port from host:port
            var hostPortParts = hostPort.Split(':');
            if (hostPortParts.Length > 1 && int.TryParse(hostPortParts[1], out var parsedPort))
            {
                port = parsedPort;
            }

            // Look for password in options
            foreach (var part in parts.Skip(1))
            {
                var option = part.Trim();
                if (option.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    password = option.Substring(9);
                }
            }
        }
        catch
        {
            // If parsing fails, use defaults
        }

        return (port, password);
    }
}
