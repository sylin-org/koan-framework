using Koan.Canon.Domain.Runtime;
using Koan.Core;
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
public sealed class CanonSampleModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Register the Customer pipeline configurator
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonRuntimeConfigurator, CustomerPipelineRegistrar>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Customer pipeline sample");
        module.AddSetting("pipeline", "Customer (Validation → Enrichment)");
        module.AddSetting("entity", "Customer (CanonEntity)");
    }
}
