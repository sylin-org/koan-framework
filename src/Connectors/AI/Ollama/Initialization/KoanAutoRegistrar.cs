using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Adapters;
using Koan.Core;
using Koan.Core.Modules;
using Koan.ZenGarden.Core;

namespace Koan.AI.Connector.Ollama.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Connector.Ollama";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register options
        services.AddKoanOptions<OllamaOptions>("Koan:Ai:Ollama");

        // Register the adapter contributor for zero-config initialization
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiAdapterContributor, OllamaAdapterContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IZenGardenOfferingBinding, OllamaZenGardenOfferingBinding>());
    }

    public void Describe(Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
    }
}
