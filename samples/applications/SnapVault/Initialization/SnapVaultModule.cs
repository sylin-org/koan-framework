using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.AI;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Media.Web.Routing;
using Koan.Web.Hosting;
using Koan.ZenGarden;
using SnapVault.Configuration;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Initialization;

/// <summary>
/// SnapVault's single self-describing boot unit (ARCH-0086). Owns config binding, boot-time seeding of the
/// system analysis-style library, the ZenGarden model-advisor log line, and the boot self-report. Domain
/// services (photo processing, set queries, prompt factory, embedding monitor) are wired in later steps.
/// </summary>
public sealed class SnapVaultModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind SnapVault:Collections -> CollectionOptions (was services.Configure<>(...) in the old Program.cs).
        services.AddKoanOptions<CollectionOptions>("SnapVault:Collections");

        // Studio↔client lifecycle (SEC-0007 P5 dogfood): invite→accept→grant, the guest ambient-scope resolver,
        // proofing, and "delete client & prove it". GalleryInviteService/SnapVaultDeprovisioningService ride the
        // shipped InviteAcceptanceService/DeprovisioningService (registered by Koan.Identity.Tenancy).
        services.AddSingleton<GalleryInviteService>();
        services.AddSingleton<GuestScopeService>();
        services.AddSingleton<ProofingService>();
        services.AddSingleton<SnapVaultDeprovisioningService>();

        // Media serving (D3): the framework recipe controller (GET /media/{id}/{recipe}, Reference=Intent via
        // Koan.Media.Web) resolves source bytes through this IMediaSource. MediaEntitySource<PhotoAsset> resolves
        // the id via PhotoAsset.Get — so media serving inherits the SEC-0008 access axis + tenant isolation
        // structurally: a guest serves only their granted events, a subject-less request fails closed (404).
        // One line replaces the hand-written IMediaSource + the whole legacy derivative/serving stack.
        services.AddMediaSource<PhotoAsset>();

        // Ingest + AI pipeline (step 5a): the durable PhotoProcessingJob resolves IPhotoProcessingService to run
        // storage → EXIF → daily-event → AI vision (gallery-recipe re-source) → embedding, reporting per-stage
        // progress via ctx.Progress (read by the step-4 SSE projection). The prompt factory assembles the
        // style-customized vision prompt (base template + DB-driven style parameters).
        services.AddSingleton<Services.AI.IAnalysisPromptFactory, Services.AI.AnalysisPromptFactory>();
        services.AddSingleton<IPhotoProcessingService, PhotoProcessingService>();

        // Read/query surface (step 5b): the session-windowed grid (#5 /photosets/query).
        services.AddSingleton<PhotoSetService>();

        // SEC-0008 access posture (step 5e): FAIL-CLOSED everywhere (the framework default — the step-5b dev-open
        // override is GONE). Every request-path subject is now set explicitly by SnapVaultSubjectMiddleware (guest /
        // operator-via-carrier / dev-trust anonymous operator), and the ingest/AI jobs run Subject.System() — so a
        // path that reaches an [AccessScoped] read without establishing a subject sees NOTHING rather than everything.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, SnapVaultSubjectContributor>());

        // §9.7 structural delete-cleanup belongs to the host composition, not mutable process state.
        PhotoAssetCleanup.Register();
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SnapVault");

        // ZenGarden model advisor (zero-config model selection). Reads AppHost.Current, which Program.cs sets
        // synchronously before await app.RunAsync() — so it is available by the time Start runs in host startup.
        var vision = ZenGarden.RecommendedModel(AiCapability.Vision);
        var embedding = ZenGarden.RecommendedModel(AiCapability.Embed);
        var chat = ZenGarden.RecommendedModel(AiCapability.Chat);
        logger.LogInformation(
            "ZenGarden model advisor: vision={Vision}, embedding={Embedding}, chat={Chat}",
            vision ?? "(pending)", embedding ?? "(pending)", chat ?? "(pending)");

        // Seed the [HostScoped] system analysis styles (platform-shared, un-scoped — no Tenant.Use wrapper).
        await AnalysisStyleSeeder.SeedDefaultStyles(logger);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("SnapVault", b => b.Value(
            "photo studio sample — seeds the system analysis-style library + logs the ZenGarden model advisor at boot"));
    }
}
