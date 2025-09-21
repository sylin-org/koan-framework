using Microsoft.Extensions.Configuration;
using Koan.Core;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Configuration provider that dynamically generates connection strings
/// for self-orchestrated dependencies
/// </summary>
public class SelfOrchestrationConfigurationProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        var sessionId = KoanEnv.SessionId;

        // Generate connection strings for self-orchestrated dependencies
        // These will be picked up by the enhanced option configurators

        // Postgres connection string
        Data["ConnectionStrings:postgres"] = $"Host=localhost;Port=5432;Database=koan_{sessionId};Username=koan;Password=dev123";

        // Redis connection string
        Data["ConnectionStrings:redis"] = "localhost:6379";

        // AI service endpoints
        Data["AI:Ollama:Endpoint"] = "http://localhost:11434";
        Data["AI:Weaviate:Endpoint"] = "http://localhost:8080";

        // Additional configuration for self-orchestration
        Data["Koan:Orchestration:Mode"] = "SelfOrchestrating";
        Data["Koan:Orchestration:SessionId"] = sessionId;
        Data["Koan:Orchestration:ConnectionTimeout"] = "30";
    }
}

/// <summary>
/// Configuration provider that dynamically generates connection strings
/// for Docker Compose container networking
/// </summary>
public class DockerComposeConfigurationProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        var sessionId = KoanEnv.SessionId;

        // Generate connection strings for Docker Compose container networking
        // Uses service names instead of localhost (container-to-container communication)

        // Postgres connection string - uses service name 'postgres'
        Data["ConnectionStrings:postgres"] = $"Host=postgres;Port=5432;Database=koan_{sessionId};Username=koan;Password=dev123";

        // Redis connection string - uses service name 'redis'
        Data["ConnectionStrings:redis"] = "redis:6379";

        // AI service endpoints - uses service names
        Data["AI:Ollama:Endpoint"] = "http://ollama:11434";
        Data["AI:Weaviate:Endpoint"] = "http://weaviate:8080";

        // Additional configuration for Docker Compose networking
        Data["Koan:Orchestration:Mode"] = "DockerCompose";
        Data["Koan:Orchestration:SessionId"] = sessionId;
        Data["Koan:Orchestration:NetworkMode"] = "container";
    }
}

/// <summary>
/// Configuration provider that dynamically generates connection strings
/// for Kubernetes service-based networking
/// </summary>
public class KubernetesConfigurationProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        var sessionId = KoanEnv.SessionId;
        var k8sNamespace = Configuration.Read<string?>(null, "KUBERNETES:NAMESPACE", null) ?? "default";

        // Generate connection strings for Kubernetes service networking
        // Uses fully qualified service DNS names

        // Postgres connection string - uses service name with namespace
        Data["ConnectionStrings:postgres"] = $"Host=postgres.{k8sNamespace}.svc.cluster.local;Port=5432;Database=koan_{sessionId};Username=koan;Password=dev123";

        // Redis connection string - uses service name with namespace
        Data["ConnectionStrings:redis"] = $"redis.{k8sNamespace}.svc.cluster.local:6379";

        // AI service endpoints - uses service names with namespace
        Data["AI:Ollama:Endpoint"] = $"http://ollama.{k8sNamespace}.svc.cluster.local:11434";
        Data["AI:Weaviate:Endpoint"] = $"http://weaviate.{k8sNamespace}.svc.cluster.local:8080";

        // Additional configuration for Kubernetes networking
        Data["Koan:Orchestration:Mode"] = "Kubernetes";
        Data["Koan:Orchestration:SessionId"] = sessionId;
        Data["Koan:Orchestration:NetworkMode"] = "kubernetes";
        Data["Koan:Orchestration:Namespace"] = k8sNamespace;
    }
}

/// <summary>
/// Extension methods for adding self-orchestration configuration
/// </summary>
public static class SelfOrchestrationConfigurationExtensions
{
    /// <summary>
    /// Add self-orchestration configuration provider to the configuration builder
    /// </summary>
    public static IConfigurationBuilder AddSelfOrchestrationConfiguration(this IConfigurationBuilder builder)
    {
        return builder.Add(new SelfOrchestrationConfigurationProvider());
    }

    /// <summary>
    /// Add Docker Compose configuration provider to the configuration builder
    /// </summary>
    public static IConfigurationBuilder AddDockerComposeConfiguration(this IConfigurationBuilder builder)
    {
        return builder.Add(new DockerComposeConfigurationProvider());
    }

    /// <summary>
    /// Add Kubernetes configuration provider to the configuration builder
    /// </summary>
    public static IConfigurationBuilder AddKubernetesConfiguration(this IConfigurationBuilder builder)
    {
        return builder.Add(new KubernetesConfigurationProvider());
    }
}