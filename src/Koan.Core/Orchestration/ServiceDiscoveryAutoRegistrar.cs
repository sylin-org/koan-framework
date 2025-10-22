using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
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

        // Register OrchestrationAwareServiceDiscoveryV2 to delegate to coordinator
        services.TryAddScoped<IOrchestrationAwareServiceDiscovery>(provider =>
            new OrchestrationAwareServiceDiscoveryV2(
                provider.GetRequiredService<IServiceDiscoveryCoordinator>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<ILogger<OrchestrationAwareServiceDiscoveryV2>>()));

        // Auto-register all IServiceDiscoveryAdapter implementations
        // These will be automatically injected into the coordinator
        var adapters = DiscoverServiceDiscoveryAdapters();

        foreach (var adapterType in adapters)
        {
            services.TryAddSingleton(typeof(IServiceDiscoveryAdapter), adapterType);
        }
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var adapters = DiscoverServiceDiscoveryAdapters();

        module.Describe(ModuleVersion);
        module.AddNote($"ServiceDiscoveryAdapters: {adapters.Count()}");

        foreach (var adapterType in adapters)
        {
            module.AddNote($"  • {adapterType.Name}");
        }
    }

    private IEnumerable<Type> DiscoverServiceDiscoveryAdapters()
    {
        // Use cached assemblies to discover all IServiceDiscoveryAdapter implementations
        var assemblies = AssemblyCache.Instance.GetAllAssemblies()
            .Where(a => !a.IsDynamic && !IsSystemAssembly(a))
            .ToArray();

        return assemblies
            .SelectMany(a => GetTypesFromAssembly(a))
            .Where(t => typeof(IServiceDiscoveryAdapter).IsAssignableFrom(t) &&
                       !t.IsInterface &&
                       !t.IsAbstract &&
                       t.IsClass)
            .ToArray();
    }

    private static bool IsSystemAssembly(System.Reflection.Assembly assembly)
    {
        var name = assembly.FullName ?? "";
        return name.StartsWith("System.") ||
               name.StartsWith("Microsoft.") ||
               name.StartsWith("netstandard") ||
               name.StartsWith("mscorlib");
    }

    private static Type[] GetTypesFromAssembly(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
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
}
