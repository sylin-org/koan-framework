using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Hosting;
using Koan.Web.Context;
using SnapVault.Configuration;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Initialization;

/// <summary>
/// Owns SnapVault's configuration, domain services, access posture, seed data, and boot report.
/// </summary>
public sealed class SnapVaultModule : KoanModule
{
    // PhotoAsset's embedding model is declared on the entity ([Embedding(Model = ...)]); the space it lands in
    // is declared here, because dimension and metric cannot be inferred from a model name. Stored and query
    // embeddings must share one space, so both halves are pinned together.
    private const string PhotoSpace = "snapvault-photos";
    private const string PhotoModel = "nomic-embed-text";
    private const int PhotoDimensions = 768;

    /// <summary>
    /// Declares the vector space PhotoAsset searches in. Passed to <c>AddKoan()</c> from Program.cs, which keeps
    /// the declaration next to the business module rather than in the host bootstrap.
    /// </summary>
    public static void Compose(KoanApplicationBuilder koan) =>
        koan.Data.Source("Default").Vector<PhotoAsset>(space => space
            .Name(PhotoSpace)
            .Dimensions(PhotoDimensions)
            .Model(PhotoModel));

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CollectionOptions>("SnapVault:Collections");

        // Studio-to-client lifecycle: explicit grant, proof, and integrity-checked deprovisioning.
        services.AddSingleton<GalleryGrantService>();
        services.AddSingleton<ProofingService>();
        services.AddSingleton<SnapVaultDeprovisioningService>();

        // Durable ingest and optional enrichment.
        services.AddSingleton<Services.AI.AnalysisPromptFactory>();
        services.AddSingleton<PhotoProcessingService>();

        // Session-windowed gallery queries.
        services.AddSingleton<PhotoSetService>();

        // One request contributor validates gallery links and contributes their tenant/read context.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWebContextContributor, SnapVaultContextContributor>());

        // Structural blob cleanup belongs to host composition, not mutable process state.
        PhotoAssetCleanup.Register();
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SnapVault");

        await AnalysisStyleSeeder.SeedDefaultStyles(logger);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("SnapVault", b => b.Value(
            "local-first photo studio with durable ingest, media recipes, client proofing, and optional AI/vector enrichment"));
    }
}
