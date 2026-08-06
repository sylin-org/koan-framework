using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Registry;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Identity.Infrastructure;
using Koan.Identity.Erasure;
using Koan.Identity.Reconciliation;
using Koan.Web.Auth.Domain;

namespace Koan.Identity.Initialization;

/// <summary>
/// SEC-0007 Layer 0 — lights up the durable person when <c>Koan.Identity</c> is referenced (Reference = Intent):
/// registers the reconciler, replaces Koan.Web.Auth's vestigial in-memory <see cref="IUserStore"/> /
/// <see cref="IExternalIdentityStore"/> with <c>Entity&lt;&gt;</c>-backed durable stores, and seeds offline dev
/// users under a dev-open / prod-closed posture. The reconciliation <i>trigger</i> is the discovered
/// <see cref="IdentityAuthFlowHandler"/> (cookie sign-in) when functional Web Auth is also referenced—no manual
/// wiring. Web Auth's defaults use standard <c>TryAdd</c> semantics, so durable store replacement is order-independent.
/// </summary>
public sealed class SecIdentityModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<IdentityOptions>(IdentityOptions.SectionPath);
        services.TryAddSingleton<IIdentityReconciler, IdentityReconciler>();

        // Layer 1 — management services (offline-capable; the optional Web package exposes them as APIs).
        services.TryAddSingleton<Management.SessionService>();
        services.TryAddScoped<Management.IdentityLifecycleService>();
        services.TryAddSingleton<Management.IdentityLinkService>(); // D5 — explicit account-linking

        // Layer 2 — access model: the global role binding + the contributor-pipeline effective-access resolver +
        // the bidirectional explainer. Contributors are discovered ([KoanDiscoverable]) so an external Membership
        // contributor (with Koan.Tenancy) lights up over the same resolver — graceful degradation, no fork.
        services.TryAddSingleton<Management.IdentityRoleService>();
        services.TryAddScoped<Access.EffectiveAccessResolver>();
        services.TryAddScoped<Access.AccessExplainer>();
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(Access.IEffectiveAccessContributor)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(Access.IEffectiveAccessContributor), type));

        // SEC-0009 — semantic owners compose one privacy erasure plan. Scoped registration permits an
        // application-owned contributor to use its ordinary scoped dependencies without special wiring.
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(IIdentityErasureContributor)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IIdentityErasureContributor), type));

        // Layer 3 — day-2 power: safe impersonation, JIT/time-boxed grants, tamper-evident audit.
        services.TryAddSingleton<Impersonation.ImpersonationService>();
        services.TryAddSingleton<Management.JitGrantService>();
        services.TryAddSingleton<Audit.AuditChain>();
        Audit.IdentityAuditHooks.Register();

        // Replace the in-memory stubs with durable Entity<>-backed stores. This is order-independent: Web Auth uses
        // TryAdd, so it cannot overwrite these registrations when its functional package is also present.
        services.Replace(ServiceDescriptor.Singleton<IUserStore, IdentityUserStore>());
        services.Replace(ServiceDescriptor.Singleton<IExternalIdentityStore, IdentityExternalIdentityStore>());

        // IdentityAuthFlowHandler is discovered via [KoanDiscoverable] on IKoanAuthFlowHandler — never registered here.
    }

    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        // Module startup can run alongside another host in tests or embedded compositions. Entity statics must
        // resolve through the provider this invocation was handed without replacing the process-default owner.
        using var hostScope = AppHost.PushScope(services);

        var env = services.GetRequiredService<IHostEnvironment>();
        var cfg = services.GetRequiredService<IConfiguration>();
        var options = services.GetService<IOptions<IdentityOptions>>()?.Value ?? new IdentityOptions();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Identity");

        var posture = IdentityPostureResolver.Resolve(env.IsDevelopment(), options.Posture);

        // Fail-closed (mirrors Trust/Tenancy): Open auto-seeds dev users and is legal only in Development.
        if (posture == IdentityPosture.Open && !env.IsDevelopment())
            throw new InvalidOperationException(
                "SEC-0007 fail-closed: Identity posture 'Open' (which auto-seeds dev users) is only valid in " +
                $"Development; environment '{env.EnvironmentName}' must resolve 'Closed'. Remove the " +
                "'Koan:Identity:Posture' override.");

        logger?.LogInformation("Koan.Identity posture resolved: {Posture}.", posture);

        if (ShouldSeedDevUsers(env.IsDevelopment(), posture, options.SeedDevUsers))
        {
            var reconciler = services.GetService<IIdentityReconciler>() ?? new IdentityReconciler();
            var devUser = (options.DevUser ?? Environment.UserName).Trim();
            await SeedDevUsersAsync(reconciler, devUser, ct).ConfigureAwait(false);
            logger?.LogInformation(
                "Koan.Identity dev-open: seeded dev person '{DevUser}' + alice@example.com + bob@example.com (offline).", devUser);
        }
    }

    /// <summary>
    /// The dev-seed gate (SEC-0007 G): offline dev users are seeded only under Development + an Open posture, and
    /// only when seeding is enabled. Pure so the P0 acceptance spec can prove the gate without a full boot.
    /// </summary>
    internal static bool ShouldSeedDevUsers(bool isDevelopment, IdentityPosture posture, bool seedDevUsers)
        => isDevelopment && posture == IdentityPosture.Open && seedDevUsers;

    /// <summary>
    /// Seeds the offline dev persons (the primary dev user + the alice/bob "sign in as …" personas) into the real
    /// data store via the reconciler. Internal so the P0 acceptance spec can drive it directly (the full Development
    /// generic-host boot trips ASP.NET's web-only DI validation, so the seed logic is exercised here instead).
    /// </summary>
    internal static async Task SeedDevUsersAsync(IIdentityReconciler reconciler, string devUser, CancellationToken ct = default)
    {
        await reconciler.ReconcileAsync(
            new IdentityClaims(devUser, DisplayName: devUser, Email: $"{devUser}@local.dev", EmailVerified: true, Provider: "dev"), ct).ConfigureAwait(false);
        await reconciler.ReconcileAsync(
            new IdentityClaims("alice@example.com", DisplayName: "Alice Example", Email: "alice@example.com", EmailVerified: true, Provider: "dev"), ct).ConfigureAwait(false);
        await reconciler.ReconcileAsync(
            new IdentityClaims("bob@example.com", DisplayName: "Bob Example", Email: "bob@example.com", EmailVerified: true, Provider: "dev"), ct).ConfigureAwait(false);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var configured = cfg.GetValue<IdentityPosture?>($"{IdentityOptions.SectionPath}:Posture");
        var auditSnapshotMode = cfg.GetValue<Audit.IdentityAuditSnapshotMode?>(
            $"{IdentityOptions.SectionPath}:{nameof(IdentityOptions.AuditSnapshotMode)}")
            ?? Audit.IdentityAuditSnapshotMode.PrivacySafe;
        var posture = IdentityPostureResolver.Resolve(env.IsDevelopment(), configured);
        var source = configured is null ? (env.IsDevelopment() ? "dev-open" : "closed") : "override";
        module.SetSetting("Identity", b => b.Value(
            $"posture={posture} ({source}); audit={auditSnapshotMode}; erasure=preview+owner-receipt; " +
            "durable person + IUserStore/IExternalIdentityStore reconciliation; no per-MAU axis (SEC-0007 D2)"));
    }
}
