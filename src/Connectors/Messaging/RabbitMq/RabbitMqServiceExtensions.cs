using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Messaging;
using Koan.Messaging.Connector.RabbitMq.Orchestration;

namespace Koan.Messaging.Connector.RabbitMq;

/// <summary>
/// Service registration for RabbitMQ messaging provider.
/// Uses IKoanAutoRegistrar pattern for automatic discovery.
/// </summary>
public class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Messaging.Connector.RabbitMq";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();
    
    public void Initialize(IServiceCollection services)
    {
        // Register RabbitMQ as a messaging provider
        services.TryAddEnumerable(ServiceDescriptor.Transient<IMessagingProvider, RabbitMqProvider>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RabbitMqOrchestrationEvaluator>());

        // Add core messaging if not already added
        services.AddKoanMessaging();
    }
    
    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Use centralized orchestration-aware service discovery
        var serviceDiscovery = new OrchestrationAwareServiceDiscovery(cfg);

        try
        {
            // Create RabbitMQ-specific discovery options
            var discoveryOptions = ServiceDiscoveryExtensions.ForRabbitMQ();

            // Add legacy environment variable support
            var envCandidates = GetLegacyEnvironmentCandidates();
            if (envCandidates.Length > 0)
            {
                discoveryOptions = discoveryOptions with
                {
                    AdditionalCandidates = envCandidates
                };
            }

            // Discover RabbitMQ service
            var discoveryTask = serviceDiscovery.DiscoverServiceAsync("rabbitmq", discoveryOptions);
            var result = discoveryTask.GetAwaiter().GetResult();

            var method = $"orchestration-{result.DiscoveryMethod}";
            var endpoint = Koan.Core.Redaction.DeIdentify(result.ServiceUrl ?? string.Empty);
            report.AddSetting("Discovery.Method", method);
            report.AddSetting("Discovery.Endpoint", endpoint);
            report.AddSetting("Discovery.Healthy", result.IsHealthy.ToString());

            var status = result.IsHealthy ? "reachable" : "unreachable";
            report.AddNote($"RabbitMQ endpoint {status}: {endpoint}");

            // Log provider election decision
            var availableProviders = DiscoverAvailableMessagingProviders();
            report.AddSetting("Provider.Selected", "RabbitMQ");
            report.AddSetting("Provider.Candidates", string.Join(", ", availableProviders));
            report.AddSetting("Provider.Rationale", "highest priority provider with orchestration-aware discovery");
        }
        catch (Exception ex)
        {
            var fallbackEndpoint = Koan.Core.Redaction.DeIdentify("amqp://guest:guest@localhost:5672");
            report.AddSetting("Discovery.Method", "orchestration-fallback");
            report.AddSetting("Discovery.Endpoint", fallbackEndpoint);
            report.AddSetting("Discovery.Healthy", bool.FalseString);
            report.AddNote($"RabbitMQ endpoint unreachable: {fallbackEndpoint}");
            report.AddSetting("Discovery.Error", ex.Message);
        }

        report.AddSetting("Priority", "100 (High - preferred provider)");
        report.AddSetting("OrchestrationMode", KoanEnv.OrchestrationMode.ToString());
        report.AddSetting("Configuration", "Orchestration-aware service discovery enabled");
    }
    
    private static string[] GetLegacyEnvironmentCandidates()
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var envUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            candidates.Add(envUrl);
        }

        var koanEnvUrl = Environment.GetEnvironmentVariable("Koan_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(koanEnvUrl))
        {
            candidates.Add(koanEnvUrl);
        }

        return candidates.ToArray();
    }

    private static string[] DiscoverAvailableMessagingProviders()
    {
        // Scan loaded assemblies for other messaging providers
        var providers = new List<string> { "RabbitMQ" }; // Always include self

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("Koan.Messaging.") && name != "Koan.Messaging.Connector.RabbitMq" && name != "Koan.Messaging.Core")
            {
                var providerName = name.Substring("Koan.Messaging.".Length);
                providers.Add(providerName);
            }
        }

        // InMemory is always available as fallback
        if (!providers.Contains("InMemory"))
            providers.Add("InMemory");

        return providers.ToArray();
    }
}
