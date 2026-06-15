using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Messaging;
using Koan.Messaging.Connector.RabbitMq.Discovery;
using Koan.Messaging.Connector.RabbitMq.Orchestration;
using Koan.Core.Hosting.Bootstrap;

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

        // Register the autonomous service-discovery adapter (ARCH-0087)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, RabbitMqDiscoveryAdapter>());

        // Add core messaging if not already added
        services.AddKoanMessaging();
    }
    
    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        try
        {
            // ARCH-0087: the Describe() boot report has no IServiceProvider, so construct the
            // autonomous discovery adapter directly (same resolution logic as the runtime path).
            var adapter = new RabbitMqDiscoveryAdapter(cfg, NullLogger<RabbitMqDiscoveryAdapter>.Instance);
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                Configuration = cfg,
                // ARCH-0087: boot report resolves the candidate only — NO blocking live AMQP connect here
                // (V1 did none; a real connect would stall boot up to HealthCheckTimeout if the broker is
                // unreachable). The adapter's AMQP health check stays available for opt-in callers.
                RequireHealthValidation = false
            };

            // Discover RabbitMQ service
            var result = adapter.Discover(context).GetAwaiter().GetResult();

            var method = $"orchestration-{result.DiscoveryMethod}";
            var endpoint = Koan.Core.Redaction.DeIdentify(result.ServiceUrl ?? "");
            module.AddSetting("Discovery.Method", method);
            module.AddSetting("Discovery.Endpoint", endpoint);
            module.AddSetting("Discovery.Healthy", result.IsHealthy.ToString());

            var status = result.IsHealthy ? "reachable" : "unreachable";
            module.AddNote($"RabbitMQ endpoint {status}: {endpoint}");

            // Log provider election decision
            var availableProviders = DiscoverAvailableMessagingProviders();
            module.AddSetting("Provider.Selected", "RabbitMQ");
            module.AddSetting("Provider.Candidates", string.Join(", ", availableProviders));
            module.AddSetting("Provider.Rationale", "highest priority provider with orchestration-aware discovery");
        }
        catch (Exception ex)
        {
            var fallbackEndpoint = Koan.Core.Redaction.DeIdentify("amqp://guest:guest@localhost:5672");
            module.AddSetting("Discovery.Method", "orchestration-fallback");
            module.AddSetting("Discovery.Endpoint", fallbackEndpoint);
            module.AddSetting("Discovery.Healthy", bool.FalseString);
            module.AddNote($"RabbitMQ endpoint unreachable: {fallbackEndpoint}");
            module.AddSetting("Discovery.Error", ex.Message);
        }

        module.AddSetting("Priority", "100 (High - preferred provider)");
        module.AddSetting("OrchestrationMode", KoanEnv.OrchestrationMode.ToString());
        module.AddSetting("Configuration", "Orchestration-aware service discovery enabled");
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

