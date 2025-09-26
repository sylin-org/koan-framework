using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Messaging;
using Koan.Messaging.RabbitMq.Orchestration;

namespace Koan.Messaging.RabbitMq;

/// <summary>
/// Service registration for RabbitMQ messaging provider.
/// Uses IKoanAutoRegistrar pattern for automatic discovery.
/// </summary>
public class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Messaging.RabbitMq";
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

            report.AddDiscovery($"orchestration-{result.DiscoveryMethod}", result.ServiceUrl);
            report.AddConnectionAttempt("Messaging.RabbitMq", result.ServiceUrl, result.IsHealthy);

            // Log provider election decision
            var availableProviders = DiscoverAvailableMessagingProviders();
            report.AddProviderElection("Messaging", "RabbitMQ", availableProviders,
                "highest priority provider with orchestration-aware discovery");
        }
        catch (Exception ex)
        {
            report.AddDiscovery("orchestration-fallback", "amqp://guest:guest@localhost:5672");
            report.AddConnectionAttempt("Messaging.RabbitMq", "amqp://guest:guest@localhost:5672", false);
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
            if (name.StartsWith("Koan.Messaging.") && name != "Koan.Messaging.RabbitMq" && name != "Koan.Messaging.Core")
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