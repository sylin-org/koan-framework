using Koan.AI.Initialization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GardenCoopEmbedded.Initialization;

/// <summary>Owns the sample's starter produce and explains its local semantic-search promise.</summary>
[After(typeof(AiModule))]
public sealed class GardenCoopEmbeddedModule : KoanModule
{
    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<GardenCoopEmbeddedModule>();
        return ProduceCatalog.EnsureStarterListings(logger, ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Five local produce listings with in-process semantic search.");
        module.AddSetting("Meaningful query", "ripe red tomato → Heirloom Tomatoes");
        module.AddNote("Saving Produce creates its embedding and local vector index automatically.");
    }
}
