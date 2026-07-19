using Koan.AI.Initialization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnimeRecommendations.Initialization;

/// <summary>Owns the starter catalog and explains the application's recommendation posture.</summary>
[After(typeof(AiModule))]
public sealed class AnimeRecommendationsModule : KoanModule
{
    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<AnimeRecommendationsModule>();
        await StarterCatalog.Ensure(logger, ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Local, explainable anime discovery shaped by a viewer's ratings and mood.");
        module.AddSetting("starter-viewer", "Mika · three ratings ready for first-run recommendations");
        module.AddSetting("recommendation", "bounded rating intent → local embedding → vector search → explanations");
        module.AddNote("Saving Anime creates its local embedding and vector index automatically.");
    }
}
