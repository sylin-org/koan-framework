using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.InMemory.Initialization;

/// <summary>
/// Auto-registers InMemory adapter when package is referenced.
/// Provides fallback storage with lowest priority (-100).
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.InMemory";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register singleton data store (ensures data persists across repository instances)
        services.AddSingleton<InMemoryDataStore>();

        // Register adapter factory with lowest priority (-100) to act as fallback
        services.AddSingleton<IDataAdapterFactory, InMemoryAdapterFactory>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting("Storage", "InMemory (ephemeral)");
        module.AddSetting("Priority", "-100 (fallback)");
    }
}

