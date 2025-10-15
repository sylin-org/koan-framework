using Koan.Canon.Domain.Runtime;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using S8.Canon.Pipeline;

namespace S8.Canon.Initialization;

/// <summary>
/// Auto-registers S8.Canon sample components with Koan framework.
/// Registers Customer pipeline configuration automatically when S8.Canon is referenced.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S8.Canon";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register the Customer pipeline configurator
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonRuntimeConfigurator, CustomerPipelineRegistrar>());
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion, "Customer pipeline sample");
        module.AddSetting("pipeline", "Customer (Validation → Enrichment)");
        module.AddSetting("entity", "Customer (CanonEntity)");
    }
}
