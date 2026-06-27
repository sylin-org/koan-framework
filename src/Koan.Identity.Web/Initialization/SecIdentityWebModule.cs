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
/// self-service consoles over the identity entities (no manual controller wiring). The headless core
/// (<c>Koan.Identity</c>) keeps working without this package; adding it lights up the surfaces.
/// </summary>
public sealed class SecIdentityWebModule : KoanModule
{
    public override string Id => "Koan.Identity.Web";

    public override void Register(IServiceCollection services)
    {
        // Mount the controllers from this assembly.
        services.AddKoanControllersFrom<IdentitySelfServiceController>();

        // D8 — the impersonation banner rides every response while an actor claim is present.
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<ImpersonationBannerFilter>());

        // Audit attribution: resolve the acting subject from the request principal (actor when impersonating).
        services.AddHttpContextAccessor();
        services.TryAddSingleton<Koan.Identity.IIdentityActorAccessor, HttpContextActorAccessor>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool("Identity — Self-service", "/api/identity/me",
            "Profile, sessions & devices, API tokens, connected accounts", capability: "identity.self-service");
        module.AddTool("Identity — Operator", "/api/identity/admin",
            "User list, bulk lifecycle, lifecycle-aware delete, groups", capability: "identity.operator");
    }
}
