using Koan.AI.Connector.LMStudio.Discovery;
using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.LMStudio.Initialization;

public sealed class LMStudioAiModule : KoanModule, IContributeTo<AiProviderContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<LMStudioOptions>(Constants.Section);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, LMStudioDiscoveryAdapter>());
        services.TryAddSingleton(sp => new LMStudioAdapter(
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptionsMonitor<LMStudioOptions>>()
                        .CurrentValue.RequestTimeoutSeconds)
            },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LMStudioAdapter>>(),
            sp.GetService<IOptions<Core.Adapters.AdaptersReadinessOptions>>()?.Value,
            sp.GetRequiredService<IOptionsMonitor<LMStudioOptions>>().CurrentValue));
    }

    public void Contribute(AiProviderContributionTarget target) =>
        target.Add<LMStudioAdapterContributor>(Constants.Adapter.Type);

    public override void Report(
        Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration cfg,
        Microsoft.Extensions.Hosting.IHostEnvironment env) =>
        module.Describe(Version);
}
