using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Messaging;

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
        
        // Add core messaging if not already added
        services.AddKoanMessaging();
    }
    
    public void Describe(Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        
        // NEW: Decision logging for RabbitMQ connection discovery
        var connectionAttempts = new List<(string source, string connectionString, bool available)>();
        
        // Check environment variables first
        var envUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            report.AddDiscovery("environment-RABBITMQ_URL", envUrl);
            connectionAttempts.Add(("environment", envUrl, true));
        }
        
        var KoanEnvUrl = Environment.GetEnvironmentVariable("Koan_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(KoanEnvUrl))
        {
            report.AddDiscovery("environment-Koan_RABBITMQ_URL", KoanEnvUrl);
            connectionAttempts.Add(("environment", KoanEnvUrl, true));
        }
        
        // Auto-discovery based on container environment
        string discoveredUrl;
        if (KoanEnv.InContainer)
        {
            discoveredUrl = "amqp://guest:guest@rabbitmq:5672";
            report.AddDiscovery("container-discovery", discoveredUrl);
            connectionAttempts.Add(("container-discovery", discoveredUrl, true));
        }
        else
        {
            discoveredUrl = "amqp://guest:guest@localhost:5672";
            report.AddDiscovery("localhost-fallback", discoveredUrl);
            connectionAttempts.Add(("localhost-fallback", discoveredUrl, true));
        }
        
        // Log connection attempts (we can't actually test connections during bootstrap without blocking)
        foreach (var attempt in connectionAttempts)
        {
            report.AddConnectionAttempt("Messaging.RabbitMq", attempt.connectionString, attempt.available);
        }
        
        // Log provider election decision
        var availableProviders = DiscoverAvailableMessagingProviders();
        if (connectionAttempts.Any())
        {
            report.AddProviderElection("Messaging", "RabbitMQ", availableProviders, "highest priority provider with available connections");
        }
        
        report.AddSetting("Priority", "100 (High - preferred provider)");
        report.AddSetting("DefaultContainerHost", "rabbitmq:5672");
        report.AddSetting("DefaultLocalHost", "localhost:5672");
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