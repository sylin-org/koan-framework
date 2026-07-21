using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Connector.Ollama.Discovery;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Semantics.Contributions;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.AI.Connector.Ollama.Initialization;

public sealed class OllamaAiModule : KoanModule, IContributeTo<AiProviderContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<OllamaOptions>(Infrastructure.Constants.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OllamaDiscoveryAdapter>());
        services.TryAddSingleton(sp => new OllamaAdapter(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaAdapter>>(),
            sp.GetRequiredService<IOptionsMonitor<OllamaOptions>>().CurrentValue));
    }

    public void Contribute(AiProviderContributionTarget target) =>
        target.Add<OllamaAdapterContributor>(Infrastructure.Constants.Adapter.Type);

    public override void Report(Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        module.Describe(Version);
    }
}
