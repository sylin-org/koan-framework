using Koan.Canon.Domain.Runtime;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using CustomerCanon.Pipeline;

namespace CustomerCanon.Initialization;

/// <summary>
/// Auto-registers CustomerCanon sample components with Koan framework.
/// Registers Customer pipeline configuration automatically when CustomerCanon is referenced.
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
