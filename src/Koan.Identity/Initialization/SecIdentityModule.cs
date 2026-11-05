using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Registry;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Identity.Infrastructure;
using Koan.Identity.Erasure;
using Koan.Identity.Reconciliation;
using Koan.Identity.Roles;
using Koan.Web.Auth.Domain;

namespace Koan.Identity.Initialization;

/// <summary>
/// SEC-0007 Layer 0 — lights up the durable person when <c>Koan.Identity</c> is referenced (Reference = Intent):
/// registers the reconciler and replaces Koan.Web.Auth's vestigial in-memory <see cref="IUserStore"/> /
/// <see cref="IExternalIdentityStore"/> with <c>Entity&lt;&gt;</c>-backed durable stores. A person is made at the first
/// sign-in of one of their logins (SEC-0040), the Test connector's offline ones included. The reconciliation <i>trigger</i> is the discovered
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
        services.AddKoanOptions<RoleOptions>(RoleOptions.SectionPath);
        services.TryAddSingleton<RoleBagCache>();
        services.TryAddSingleton<RoleCollection>();
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
        RoleMutationGuard.Register();
        Audit.IdentityAuditHooks.Register();

        // Replace the in-memory stubs with durable Entity<>-backed stores. This is order-independent: Web Auth uses
        // TryAdd, so it cannot overwrite these registrations when its functional package is also present.
        services.Replace(ServiceDescriptor.Singleton<IUserStore, IdentityUserStore>());
        services.Replace(ServiceDescriptor.Singleton<IExternalIdentityStore, IdentityExternalIdentityStore>());

        // IdentityAuthFlowHandler is discovered via [KoanDiscoverable] on IKoanAuthFlowHandler — never registered here.
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var auditSnapshotMode = cfg.GetValue<Audit.IdentityAuditSnapshotMode?>(
            $"{IdentityOptions.SectionPath}:{nameof(IdentityOptions.AuditSnapshotMode)}")
            ?? Audit.IdentityAuditSnapshotMode.PrivacySafe;
        module.SetSetting("Identity", b => b.Value(
            $"audit={auditSnapshotMode}; erasure=preview+owner-receipt; " +
            "durable person of Koan's own id + IUserStore/IExternalIdentityStore reconciliation; no per-MAU axis (SEC-0007 D2, SEC-0040)"));
    }
}
