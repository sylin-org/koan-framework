using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;

namespace Koan.Core.BackgroundServices;

/// <summary>
/// Auto-registrar for Koan Background Services
/// </summary>
public class KoanBackgroundServiceAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Core.BackgroundServices";
    public string? ModuleVersion => "1.0.0";

    public void Initialize(IServiceCollection services)
    {
        // Register core background service infrastructure
        services.Configure<KoanBackgroundServiceOptions>(options =>
        {
            // Set defaults
            options.Enabled = true;
            options.StartupTimeoutSeconds = 120;
            options.FailFastOnStartupFailure = true;
        });

        // Register the orchestrator as both a singleton and hosted service
        services.AddSingleton<KoanBackgroundServiceOrchestrator>();
        services.AddHostedService<KoanBackgroundServiceOrchestrator>();

        // Register service registry
        services.TryAddSingleton<IServiceRegistry, ServiceRegistry>();

        // Discover and register all background services using generated descriptors
        var discoveredServices = KoanRegistry.GetBackgroundServices();

        foreach (var serviceInfo in discoveredServices)
        {
            if (!ShouldRegisterService(serviceInfo))
                continue;

            RegisterBackgroundService(services, serviceInfo);
        }

        // Add the orchestrator as a health contributor
        services.AddSingleton<IHealthContributor>(
            provider => (IHealthContributor)provider.GetRequiredService<KoanBackgroundServiceOrchestrator>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var discoveredServices = KoanRegistry.GetBackgroundServices();
        var enabledCount = discoveredServices.Count(service => ShouldRegisterService(service));
        var totalCount = discoveredServices.Length;

        module.Describe(ModuleVersion);
        module.AddNote($"TotalServices: {totalCount}, EnabledServices: {enabledCount}");

        var serviceTypes = discoveredServices
            .GroupBy(s => s.IsStartup ? "Startup" : s.IsPeriodic ? "Periodic" : s.IsPokable ? "Pokable" : "Standard")
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var kvp in serviceTypes)
        {
            module.AddNote($"{kvp.Key}: {kvp.Value}");
        }
    }

    private bool ShouldRegisterService(KoanRegistry.BackgroundServiceDescriptor serviceInfo)
    {
        if (!serviceInfo.Enabled)
        {
            return false;
        }

        return KoanEnv.EnvironmentName switch
        {
            "Development" => serviceInfo.RunInDevelopment,
            "Production" => serviceInfo.RunInProduction,
            "Testing" => serviceInfo.RunInTesting,
            _ => true
        };
    }

    private void RegisterBackgroundService(IServiceCollection services, KoanRegistry.BackgroundServiceDescriptor serviceInfo)
    {
        var lifetime = serviceInfo.Lifetime;

        // Register the service itself
        services.Add(ServiceDescriptor.Describe(
            serviceInfo.ServiceType,
            serviceInfo.ServiceType,
            lifetime));

        // Register as IKoanBackgroundService for orchestrator discovery
        services.Add(ServiceDescriptor.Describe(
            typeof(IKoanBackgroundService),
            provider => provider.GetRequiredService(serviceInfo.ServiceType),
            lifetime));

        // Register specific interfaces if implemented
        if (serviceInfo.IsPokable)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanPokableService),
                provider => (IKoanPokableService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        if (serviceInfo.IsPeriodic)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanPeriodicService),
                provider => (IKoanPeriodicService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        if (serviceInfo.IsStartup)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanStartupService),
                provider => (IKoanStartupService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        // Register as health contributor if applicable
        if (serviceInfo.ImplementsHealthContributor)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IHealthContributor),
                provider => (IHealthContributor)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }
    }
}
