using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Adapters;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.AI.Connector.Ollama.Initialization;

public sealed class OllamaAiModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Register options
        services.AddKoanOptions<OllamaOptions>(Infrastructure.Constants.Section);

        // Register the adapter contributor for zero-config initialization
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiAdapterContributor, OllamaAdapterContributor>());
    }

    public override void Report(Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        module.Describe(Version);
    }
}
