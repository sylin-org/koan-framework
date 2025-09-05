using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Messaging;

namespace Sora.Messaging.RabbitMq;

/// <summary>
/// Service registration for RabbitMQ messaging provider.
/// Uses ISoraAutoRegistrar pattern for automatic discovery.
/// </summary>
public class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Messaging.RabbitMq";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();
    
    public void Initialize(IServiceCollection services)
    {
        // Register RabbitMQ as a messaging provider
        services.TryAddEnumerable(ServiceDescriptor.Transient<IMessagingProvider, RabbitMqProvider>());
        
        // Add core messaging if not already added
        services.AddSoraMessaging();
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
        
        var soraEnvUrl = Environment.GetEnvironmentVariable("SORA_RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(soraEnvUrl))
        {
            report.AddDiscovery("environment-SORA_RABBITMQ_URL", soraEnvUrl);
            connectionAttempts.Add(("environment", soraEnvUrl, true));
        }
        
        // Auto-discovery based on container environment
        string discoveredUrl;
        if (SoraEnv.InContainer)
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
            if (name.StartsWith("Sora.Messaging.") && name != "Sora.Messaging.RabbitMq" && name != "Sora.Messaging.Core")
            {
                var providerName = name.Substring("Sora.Messaging.".Length);
                providers.Add(providerName);
            }
        }
        
        // InMemory is always available as fallback
        if (!providers.Contains("InMemory"))
            providers.Add("InMemory");
        
        return providers.ToArray();
    }
}