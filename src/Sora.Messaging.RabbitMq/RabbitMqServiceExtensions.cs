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
        report.AddSetting("Priority", "100 (High - preferred provider)");
    }
}