using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Review.Initialization;

/// <summary>
/// Auto-registers review queue infrastructure when Koan.AI.Review is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Review";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<ReviewQueueRegistry>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Review queue infrastructure registered (human-in-the-loop feedback).");
    }
}
