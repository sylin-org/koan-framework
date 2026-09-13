using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Extensions;

namespace Koan.Identity.Web.Initialization;

/// <summary>
/// SEC-0007 Layer 1 — Reference = Intent: referencing <c>Koan.Identity.Web</c> auto-mounts the operator and
/// self-service APIs over the identity entities (no manual controller wiring). The headless core
/// (<c>Koan.Identity</c>) keeps working without this package; adding it lights up the surfaces.
/// </summary>
public sealed class SecIdentityWebModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Mount the controllers from this assembly.
        services.AddKoanControllersFrom<IdentitySelfServiceController>();
        services.AddAntiforgery();
        services.TryAddScoped<ScopedRoleExceptionFilter>();
        services.TryAddScoped<ScopedRoleAntiforgeryFilter>();

        // D8 — the impersonation banner rides every response while an actor claim is present.
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<ImpersonationBannerFilter>());

        // Audit attribution: resolve the acting subject from the request principal (actor when impersonating).
        services.AddHttpContextAccessor();
        services.TryAddSingleton<Koan.Identity.IIdentityActorAccessor, HttpContextActorAccessor>();
        services.TryAddSingleton<Koan.Identity.Roles.IScopedRoleSubjectAccessor, HttpContextScopedRoleSubjectAccessor>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool("Identity — Self-service", "/api/identity/me",
            "Profile, sessions & devices, connected accounts", capability: "identity.self-service");
        module.AddTool("Identity — Operator", "/api/identity/admin",
            "User list, bulk lifecycle, lifecycle-aware delete, access, impersonation", capability: "identity.operator");
        module.AddTool("Identity — Scoped roles", "/api/identity/scoped-roles/descriptor",
            "Scoped definitions, assignments, policy and previews", capability: "identity.scoped-roles");
    }
}
