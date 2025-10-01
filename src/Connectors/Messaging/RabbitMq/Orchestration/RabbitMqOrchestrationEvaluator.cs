using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.Messaging.Connector.RabbitMq.Orchestration;

/// <summary>
/// RabbitMQ-specific orchestration evaluator that determines if RabbitMQ containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class RabbitMqOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    public RabbitMqOrchestrationEvaluator(ILogger<RabbitMqOrchestrationEvaluator>? logger = null)
        : base(logger)
    {
    }

    public override string ServiceName => "rabbitmq";
    public override int StartupPriority => 250; // Between data services and AI services

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // RabbitMQ is enabled if there's any messaging configuration or if other services reference it
        // Check for explicit configuration or if the messaging provider is being used
        var explicitConfig = HasExplicitConfiguration(configuration);

        // For now, we'll be conservative and only enable if explicitly configured
        return explicitConfig;
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit RabbitMQ connection configuration
        var explicitConnectionString = Configuration.ReadFirst(configuration, "",
            "Koan:Messaging:RabbitMQ:ConnectionString",
            "Koan:Messaging:ConnectionString",
            "ConnectionStrings:rabbitmq",
            "ConnectionStrings:RabbitMQ",
            "ConnectionStrings:Messaging");

        return !string.IsNullOrWhiteSpace(explicitConnectionString);
    }

    protected override int GetDefaultPort()
    {
        return 5672; // Standard RabbitMQ AMQP port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var envUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            var host = ExtractHostFromConnectionString(envUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanEnvUrl = Environment.GetEnvironmentVariable("Koan_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(koanEnvUrl))
        {
            var host = ExtractHostFromConnectionString(koanEnvUrl);
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
            Logger?.LogDebug("[RabbitMQ] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Get configured credentials
            var username = Configuration.ReadFirst(configuration, "guest",
                "Koan:Messaging:RabbitMQ:Username",
                "Koan:Messaging:Username");

            var password = Configuration.ReadFirst(configuration, "guest",
                "Koan:Messaging:RabbitMQ:Password",
                "Koan:Messaging:Password");

            // Build connection string for validation
            var connectionString = BuildRabbitMqConnectionString(hostResult.HostEndpoint!, username, password);

            // Try to connect with the configured credentials
            var isValid = await TryRabbitMqConnectionAsync(connectionString);

            Logger?.LogDebug("[RabbitMQ] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[RabbitMQ] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Get configuration values
        var username = Configuration.ReadFirst(configuration, "guest",
            "Koan:Messaging:RabbitMQ:Username",
            "Koan:Messaging:Username");

        var password = Configuration.ReadFirst(configuration, "guest",
            "Koan:Messaging:RabbitMQ:Password",
            "Koan:Messaging:Password");

        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "rabbitmq",
            ["RABBITMQ_DEFAULT_USER"] = username,
            ["RABBITMQ_DEFAULT_PASS"] = password
        };

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "rabbitmq:3.13-management",
            Port = GetDefaultPort(),
            Ports = new Dictionary<int, int> { [5672] = 5672, [15672] = 15672 }, // Main port and management UI
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "rabbitmq-diagnostics -q ping",
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-rabbitmq-{context.SessionId}:/var/lib/rabbitmq"
            }
        });
    }

    private static string BuildRabbitMqConnectionString(string hostPort, string? username, string? password)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "5672";

        var auth = string.IsNullOrEmpty(username) ? "guest:guest" : $"{username}:{password ?? ""}";
        return $"amqp://{auth}@{host}:{port}";
    }

    private static string? ExtractHostFromConnectionString(string connectionString)
    {
        try
        {
            // Handle both amqp:// and plain host:port formats
            if (connectionString.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Assume it's just host:port
            return connectionString;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryRabbitMqConnectionAsync(string connectionString)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            // Verify we can actually do basic operations
            await channel.ExchangeDeclareAsync("Koan.test", ExchangeType.Direct, durable: false, autoDelete: true);
            await channel.ExchangeDeleteAsync("Koan.test");

            return true;
        }
        catch
        {
            return false;
        }
    }
}
