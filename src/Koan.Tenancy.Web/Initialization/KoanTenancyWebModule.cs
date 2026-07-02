using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Concurrency;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Controllers;
using Koan.Tenancy.Web.Services;
using Koan.Web.Extensions;

namespace Koan.Tenancy.Web.Initialization;

/// <summary>
/// ARCH-0104 — Reference = Intent: referencing <c>Koan.Tenancy.Web</c> auto-mounts the host-face tenancy
/// control-plane console (operator API + bundled UI) over the control-plane entities. The headless tenancy core
/// keeps working without it; adding this package lights up the operator surface. Ordered <c>[After]</c> the
/// tenancy core so <see cref="TenancyRuntime"/> and the control-plane registrations exist first.
/// </summary>
[After(typeof(Koan.Tenancy.Initialization.KoanAutoRegistrar))]
public sealed class KoanTenancyWebModule : KoanModule
{
    public override string Id => "Koan.Tenancy.Web";

    public override void Register(IServiceCollection services)
    {
        // Mount the operator API + UI controllers from this assembly (Reference = Intent).
        services.AddKoanControllersFrom<TenancyOperatorController>();

        // The lifecycle actions (audited by construction). The keyed lease gate serializes per-tenant owner-seat
        // revocations (Reference = Intent — idempotent registration).
        services.AddKoanKeyedLeaseGate();
        services.TryAddSingleton<TenantLifecycleService>();

        // The posture-aware operator gate (dev-open just-works; prod-closed requires the explicit host role and
        // fails closed). The handler registration is idempotent (TryAddEnumerable), and the policy is added only if
        // absent — so referencing this module adds ONE named policy without redefining an app's own policies (it
        // does not touch Default/Fallback policies).
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, OperatorAuthorizationHandler>());
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy(TenancyWebPolicies.Operator) is null)
                options.AddPolicy(TenancyWebPolicies.Operator, policy => policy.AddRequirements(new OperatorRequirement()));
        });
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool("Tenancy — Operator console", "/tenancy",
            "Fleet roster, tenant lifecycle (suspend/reactivate, invite/revoke, erase), operations feed & audit log",
            capability: "tenancy.operator");
        module.AddTool("Tenancy — Operator API", "/api/tenancy/admin",
            "Host-face control-plane projection + guarded lifecycle actions", capability: "tenancy.operator");
    }
}
