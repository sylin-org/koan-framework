using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;

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

        // Discover and register all background services
        var discoveredServices = DiscoverBackgroundServices();

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

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var discoveredServices = DiscoverBackgroundServices();
        var enabledCount = discoveredServices.Count(s => ShouldRegisterService(s));
        var totalCount = discoveredServices.Count();

        report.AddModule(ModuleName, ModuleVersion);
        report.AddNote($"TotalServices: {totalCount}, EnabledServices: {enabledCount}");
        var serviceTypes = discoveredServices.GroupBy(s => s.ServiceType switch
        {
            var t when typeof(IKoanStartupService).IsAssignableFrom(t) => "Startup",
            var t when typeof(IKoanPeriodicService).IsAssignableFrom(t) => "Periodic",
            var t when typeof(IKoanPokableService).IsAssignableFrom(t) => "Pokable",
            _ => "Standard"
        }).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kvp in serviceTypes)
            report.AddNote($"{kvp.Key}: {kvp.Value}");
    }

    private IEnumerable<BackgroundServiceInfo> DiscoverBackgroundServices()
    {
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies()
            .Where(a => !a.IsDynamic && !IsSystemAssembly(a))
            .ToArray();

        return assemblies
            .SelectMany(a => GetTypesFromAssembly(a))
            .Where(t => typeof(IKoanBackgroundService).IsAssignableFrom(t) && 
                       !t.IsInterface && 
                       !t.IsAbstract && 
                       t.IsClass)
            .Select(t => new BackgroundServiceInfo
            {
                ServiceType = t,
                Attribute = t.GetCustomAttribute<KoanBackgroundServiceAttribute>(),
                IsPeriodicService = typeof(IKoanPeriodicService).IsAssignableFrom(t),
                IsStartupService = typeof(IKoanStartupService).IsAssignableFrom(t),
                IsPokableService = typeof(IKoanPokableService).IsAssignableFrom(t)
            })
            .Where(info => info.Attribute?.Enabled ?? true);
    }

    private bool ShouldRegisterService(BackgroundServiceInfo serviceInfo)
    {
        var attr = serviceInfo.Attribute;
        if (attr == null) return true;

            return KoanEnv.EnvironmentName switch
        {
            "Development" => attr.RunInDevelopment,
            "Production" => attr.RunInProduction,
            "Testing" => attr.RunInTesting,
            _ => true
        };
    }

    private void RegisterBackgroundService(IServiceCollection services, BackgroundServiceInfo serviceInfo)
    {
        var lifetime = serviceInfo.Attribute?.Lifetime ?? ServiceLifetime.Singleton;

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
        if (serviceInfo.IsPokableService)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanPokableService),
                provider => (IKoanPokableService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        if (serviceInfo.IsPeriodicService)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanPeriodicService),
                provider => (IKoanPeriodicService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        if (serviceInfo.IsStartupService)
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IKoanStartupService),
                provider => (IKoanStartupService)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }

        // Register as health contributor if applicable
        if (typeof(IHealthContributor).IsAssignableFrom(serviceInfo.ServiceType))
        {
            services.Add(ServiceDescriptor.Describe(
                typeof(IHealthContributor),
                provider => (IHealthContributor)provider.GetRequiredService(serviceInfo.ServiceType),
                lifetime));
        }
    }

    private static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.FullName ?? "";
        return name.StartsWith("System.") ||
               name.StartsWith("Microsoft.") ||
               name.StartsWith("netstandard") ||
               name.StartsWith("mscorlib");
    }

    private static Type[] GetTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return only the types that loaded successfully
            return ex.Types.Where(t => t != null).ToArray()!;
        }
        catch
        {
            // If we can't load types from this assembly, skip it
            return Array.Empty<Type>();
        }
    }

    private record BackgroundServiceInfo
    {
        public Type ServiceType { get; init; } = null!;
        public KoanBackgroundServiceAttribute? Attribute { get; init; }
        public bool IsPeriodicService { get; init; }
        public bool IsStartupService { get; init; }
        public bool IsPokableService { get; init; }
    }
}