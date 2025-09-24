using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Core.Orchestration;

/// <summary>
/// Extension methods to simplify service discovery for common adapter scenarios.
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// Create service discovery options for database-style services.
    /// </summary>
    public static ServiceDiscoveryOptions ForDatabase(
        string serviceName,
        int defaultPort,
        string? databaseName = null,
        string? username = null,
        string? password = null)
    {
        var hints = new OrchestrationConnectionHints
        {
            ServiceName = serviceName,
            DefaultPort = defaultPort,
            SelfOrchestrated = BuildConnectionString(serviceName, "localhost", defaultPort, databaseName, username, password),
            DockerCompose = BuildConnectionString(serviceName, serviceName, defaultPort, databaseName, username, password),
            Kubernetes = BuildConnectionString(serviceName, $"{serviceName}.default.svc.cluster.local", defaultPort, databaseName, username, password),
            AspireManaged = null, // Aspire provides via service discovery
            External = null       // Must be explicitly configured
        };

        return new ServiceDiscoveryOptions
        {
            UrlHints = hints,
            ExplicitConfigurationSections = new[]
            {
                $"Koan:Data:{serviceName}",
                "Koan:Data",
                "ConnectionStrings"
            }
        };
    }

    /// <summary>
    /// Create service discovery options for HTTP API services.
    /// </summary>
    public static ServiceDiscoveryOptions ForHttpService(
        string serviceName,
        int defaultPort,
        string? healthCheckPath = null,
        TimeSpan? healthCheckTimeout = null)
    {
        var hints = new OrchestrationConnectionHints
        {
            ServiceName = serviceName,
            DefaultPort = defaultPort,
            SelfOrchestrated = $"http://localhost:{defaultPort}",
            DockerCompose = $"http://{serviceName}:{defaultPort}",
            Kubernetes = $"http://{serviceName}.default.svc.cluster.local:{defaultPort}",
            AspireManaged = null, // Aspire provides via service discovery
            External = null       // Must be explicitly configured
        };

        var healthCheck = string.IsNullOrEmpty(healthCheckPath) ? null : new HealthCheckOptions
        {
            HealthCheckPath = healthCheckPath,
            Timeout = healthCheckTimeout ?? TimeSpan.FromMilliseconds(500),
            Required = true
        };

        return new ServiceDiscoveryOptions
        {
            UrlHints = hints,
            HealthCheck = healthCheck,
            ExplicitConfigurationSections = new[]
            {
                $"Koan:Services:{serviceName}",
                $"Koan:AI:{serviceName}",
                "Koan:Services",
                "ConnectionStrings"
            }
        };
    }

    /// <summary>
    /// Create service discovery options for MongoDB with intelligent connection string building.
    /// </summary>
    public static ServiceDiscoveryOptions ForMongoDB(
        string? databaseName = null,
        string? username = null,
        string? password = null)
    {
        return ForDatabase("mongodb", 27017, databaseName, username, password);
    }

    /// <summary>
    /// Create service discovery options for Ollama AI service.
    /// </summary>
    public static ServiceDiscoveryOptions ForOllama()
    {
        return ForHttpService("ollama", 11434, "/api/tags");
    }

    /// <summary>
    /// Create service discovery options for Weaviate vector database.
    /// </summary>
    public static ServiceDiscoveryOptions ForWeaviate()
    {
        return ForHttpService("weaviate", 8080, "/v1/.well-known/ready");
    }

    /// <summary>
    /// Create service discovery options for RabbitMQ message broker.
    /// </summary>
    public static ServiceDiscoveryOptions ForRabbitMQ(
        string? username = null,
        string? password = null)
    {
        return ForDatabase("rabbitmq", 5672, username: username, password: password);
    }

    /// <summary>
    /// Create service discovery options for HashiCorp Vault.
    /// </summary>
    public static ServiceDiscoveryOptions ForVault()
    {
        return ForHttpService("vault", 8200, "/v1/sys/health");
    }

    /// <summary>
    /// Quick helper to discover a service URL for HTTP services.
    /// </summary>
    public static async Task<string> DiscoverServiceUrlAsync(
        this IOrchestrationAwareServiceDiscovery discovery,
        string serviceName,
        int defaultPort,
        string? healthCheckPath = null,
        CancellationToken cancellationToken = default)
    {
        var options = ForHttpService(serviceName, defaultPort, healthCheckPath);
        var result = await discovery.DiscoverServiceAsync(serviceName, options, cancellationToken);
        return result.ServiceUrl;
    }

    /// <summary>
    /// Quick helper to resolve a database connection string.
    /// </summary>
    public static string ResolveConnectionString(
        this IOrchestrationAwareServiceDiscovery discovery,
        string serviceName,
        int defaultPort,
        string? databaseName = null,
        string? username = null,
        string? password = null)
    {
        var hints = new OrchestrationConnectionHints
        {
            ServiceName = serviceName,
            DefaultPort = defaultPort,
            SelfOrchestrated = BuildConnectionString(serviceName, "localhost", defaultPort, databaseName, username, password),
            DockerCompose = BuildConnectionString(serviceName, serviceName, defaultPort, databaseName, username, password),
            Kubernetes = BuildConnectionString(serviceName, $"{serviceName}.default.svc.cluster.local", defaultPort, databaseName, username, password),
            AspireManaged = null,
            External = null
        };

        return discovery.ResolveConnectionString(serviceName, hints);
    }

    private static string BuildConnectionString(
        string serviceName,
        string hostname,
        int port,
        string? databaseName,
        string? username,
        string? password)
    {
        return serviceName.ToLowerInvariant() switch
        {
            "mongodb" or "mongo" => BuildMongoConnectionString(hostname, port, databaseName, username, password),
            "postgres" or "postgresql" => BuildPostgresConnectionString(hostname, port, databaseName, username, password),
            "rabbitmq" => BuildRabbitMQConnectionString(hostname, port, username, password),
            _ => $"{hostname}:{port}"
        };
    }

    private static string BuildMongoConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
        return $"mongodb://{auth}{hostname}:{port}{db}";
    }

    private static string BuildPostgresConnectionString(string hostname, int port, string? database, string? username, string? password)
    {
        var parts = new List<string> { $"Host={hostname}", $"Port={port}" };
        if (!string.IsNullOrEmpty(database)) parts.Add($"Database={database}");
        if (!string.IsNullOrEmpty(username)) parts.Add($"Username={username}");
        if (!string.IsNullOrEmpty(password)) parts.Add($"Password={password}");
        return string.Join(";", parts);
    }

    private static string BuildRabbitMQConnectionString(string hostname, int port, string? username, string? password)
    {
        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        return $"amqp://{auth}{hostname}:{port}";
    }
}