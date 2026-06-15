using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Orchestration;

/// <summary>
/// Auto-registrar for service discovery infrastructure.
/// Registers coordinator and discovers all service discovery adapters.
/// </summary>
public class ServiceDiscoveryAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Core.ServiceDiscovery";
    public string? ModuleVersion => "1.0.0";

    public void Initialize(IServiceCollection services)
    {
        // Register the service discovery coordinator as singleton
        services.TryAddSingleton<IServiceDiscoveryCoordinator, ServiceDiscoveryCoordinator>();

        // Register OrchestrationAwareServiceDiscovery to delegate to coordinator
        services.TryAddScoped<IOrchestrationAwareServiceDiscovery>(provider =>
            new OrchestrationAwareServiceDiscovery(
                provider.GetRequiredService<IServiceDiscoveryCoordinator>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<ILogger<OrchestrationAwareServiceDiscovery>>()));

        // Auto-register all IServiceDiscoveryAdapter implementations discovered at compile time
        var adapters = KoanRegistry.GetServiceDiscoveryAdapters();

        foreach (var adapter in adapters)
        {
            services.TryAddSingleton(typeof(IServiceDiscoveryAdapter), adapter.ServiceType);
        }
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var adapters = KoanRegistry.GetServiceDiscoveryAdapters();

        module.Describe(ModuleVersion);
        module.AddNote($"ServiceDiscoveryAdapters: {adapters.Length}");

        foreach (var adapter in adapters)
        {
            module.AddNote($"  • {adapter.ServiceType.Name}");
        }
    }
}
