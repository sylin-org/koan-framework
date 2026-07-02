using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Concurrency;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Controllers;
using Koan.Tenancy.Web.Hosting;
using Koan.Tenancy.Web.Services;
using Koan.Web.Extensions;
using Koan.Web.Hosting;

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

        // The layered activation config (Koan:Tenancy:Console) + the EXPOSURE layer: a BeforeRouting middleware that
        // 404s a console request failing the kill-switch / host allow-list / required header (routing, not authority).
        services.AddKoanOptions<TenancyConsoleOptions>(TenancyConsoleOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TenancyConsoleExposureContributor>());

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
        module.AddTool("Tenancy — Operator console", TenancyConsolePaths.UiPath,
            "Fleet roster, tenant lifecycle (suspend/reactivate, invite/revoke, erase), operations feed & audit log",
            capability: "tenancy.operator");
        module.AddTool("Tenancy — Operator API", TenancyConsolePaths.ApiPath,
            "Host-face control-plane projection + guarded lifecycle actions", capability: "tenancy.operator");

        // Self-announce the RESOLVED activation so "how do I make it appear?" is a boot-report line, not a support
        // ticket (read from config with no Binder dependency — indexer + GetChildren are always available).
        var section = TenancyConsoleOptions.SectionPath;
        var enabled = !string.Equals(cfg[$"{section}:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        // Report the RESOLVED posture the gate actually uses (honoring the Koan:Data:Tenancy:Posture override), not a
        // naive env check — otherwise a dev host with Posture=Closed would announce "just-works" while the gate 403s.
        var postureOverride = Enum.TryParse<TenancyPosture>(cfg["Koan:Data:Tenancy:Posture"], ignoreCase: true, out var p)
            ? (TenancyPosture?)p : null;
        var posture = TenancyPostureResolver.Resolve(env.IsDevelopment(), postureOverride) == TenancyPosture.Open
            ? "Open (dev — just-works)"
            : "Closed (requires grant, fail-closed)";
        var operators = cfg.GetSection($"{section}:Grant:Operators").GetChildren().Count();
        var hosts = cfg.GetSection($"{section}:Exposure:Hosts").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        var header = cfg[$"{section}:Exposure:RequireHeader"];

        var exposure = enabled
            ? $"host={(hosts.Length == 0 ? "any" : string.Join(",", hosts))}{(string.IsNullOrWhiteSpace(header) ? "" : $" · header={header}")}"
            : "DISABLED (kill-switch)";
        module.SetSetting("Tenancy console activation", b => b.Value(
            $"{TenancyConsolePaths.UiPath} · posture={posture} · exposure={exposure} · operators-configured={operators} (grant = allow-list ∪ role)"));
    }
}
