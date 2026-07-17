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
public sealed class AiReviewModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<ReviewQueueRegistry>();
        services.AddSingleton<IReviewActionHandler, ReviewActionHandler>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Review queue infrastructure registered (human-in-the-loop feedback).");
    }
}
