using Koan.AI.Connector.LlamaCpp.Discovery;
using Koan.AI.Connector.LlamaCpp.Infrastructure;
using Koan.AI.Connector.LlamaCpp.Options;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.LlamaCpp.Initialization;

public sealed class LlamaCppAiModule : KoanModule, IContributeTo<AiProviderContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<LlamaCppOptions>(Constants.Section);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, LlamaCppDiscoveryAdapter>());
        services.TryAddSingleton(sp => new LlamaCppAdapter(
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptionsMonitor<LlamaCppOptions>>()
                        .CurrentValue.RequestTimeoutSeconds)
            },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LlamaCppAdapter>>(),
            sp.GetService<IOptions<Core.Adapters.AdaptersReadinessOptions>>()?.Value,
            sp.GetRequiredService<IOptionsMonitor<LlamaCppOptions>>().CurrentValue));
    }

    public void Contribute(AiProviderContributionTarget target) =>
        target.Add<LlamaCppAdapterContributor>(Constants.Adapter.Type);

    public override void Report(
        Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration cfg,
        Microsoft.Extensions.Hosting.IHostEnvironment env) =>
        module.Describe(Version);
}
