using Microsoft.Extensions.Configuration;

namespace Koan.Core.Orchestration;

/// <summary>
/// Default implementation of orchestration-aware connection string resolution.
/// Uses KoanEnv.OrchestrationMode to select appropriate connection strategies.
/// </summary>
public sealed class OrchestrationAwareConnectionResolver : IOrchestrationAwareConnectionResolver
{
    private readonly IConfiguration? _configuration;

    public OrchestrationAwareConnectionResolver(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    public string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints)
    {
        // First check for Aspire connection string (highest priority)
        if (_configuration != null)
        {
            var aspireConnectionString = _configuration.GetConnectionString(serviceName);
            if (!string.IsNullOrEmpty(aspireConnectionString))
            {
                return aspireConnectionString;
            }
        }

        // Check for explicit configuration override
        if (_configuration != null)
        {
            var explicitConnectionString = Configuration.ReadFirst(_configuration, "",
                $"Koan:Data:{hints.ServiceName ?? serviceName}:ConnectionString",
                $"Koan:Data:ConnectionString",
                $"ConnectionStrings:{serviceName}");
            if (!string.IsNullOrWhiteSpace(explicitConnectionString))
            {
                return explicitConnectionString;
            }
        }

        // Use orchestration mode to determine connection strategy
        var orchestrationMode = KoanEnv.OrchestrationMode;

        return orchestrationMode switch
        {
            OrchestrationMode.SelfOrchestrating =>
                hints.SelfOrchestrated ?? $"localhost:{hints.DefaultPort}",

            OrchestrationMode.DockerCompose =>
                hints.DockerCompose ?? $"{hints.ServiceName ?? serviceName}:{hints.DefaultPort}",

            OrchestrationMode.Kubernetes =>
                hints.Kubernetes ?? $"{hints.ServiceName ?? serviceName}.default.svc.cluster.local:{hints.DefaultPort}",

            OrchestrationMode.AspireAppHost =>
                hints.AspireManaged ?? throw new InvalidOperationException(
                    $"Aspire mode detected but no Aspire connection string found for service '{serviceName}'. " +
                    "Ensure the service is registered in the Aspire AppHost."),

            OrchestrationMode.Standalone =>
                hints.External ?? throw new InvalidOperationException(
                    $"Standalone mode detected but no external connection string configured for service '{serviceName}'. " +
                    "Configure connection string in appsettings or environment variables."),

            _ => throw new InvalidOperationException(
                $"Unknown orchestration mode: {orchestrationMode}")
        };
    }
}