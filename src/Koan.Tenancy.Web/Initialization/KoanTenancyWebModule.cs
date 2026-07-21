using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
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
/// registry and membership console (operator API + bundled UI) over the control-plane entities. The headless tenancy
/// core keeps working without it; adding this package lights up the operator surface.
/// </summary>
public sealed class KoanTenancyWebModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Mount the operator API + UI controllers from this assembly (Reference = Intent).
        services.AddKoanControllersFrom<TenancyOperatorController>();

        // The layered activation config (Koan:Tenancy:Console) + the EXPOSURE layer: a BeforeRouting middleware that
        // 404s a console request failing the kill-switch / host allow-list / required header (routing, not authority).
        services.AddKoanOptions<TenancyConsoleOptions>(TenancyConsoleOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TenancyConsoleExposureContributor>());

        services.TryAddSingleton<TenantAdministrationService>();

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

        services.AddOptions<TenancyConsoleOptions>()
            .Validate(options => options.AuditPageSize > 0, "AuditPageSize must be greater than zero.")
            .Validate(options => options.MaxAuditPageSize >= options.AuditPageSize,
                "MaxAuditPageSize must be greater than or equal to AuditPageSize.")
            .ValidateOnStart();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddTool("Tenancy — Operator console", TenancyConsolePaths.UiPath,
            "Tenant registry, membership grants and audit",
            capability: "tenancy.operator");
        module.AddTool("Tenancy — Operator API", TenancyConsolePaths.ApiPath,
            "Host-authorized tenant registry and membership administration", capability: "tenancy.operator");

        // Self-announce the RESOLVED activation so "how do I make it appear?" is a boot-report line, not a support
        // ticket (read from config with no Binder dependency — indexer + GetChildren are always available).
        var section = TenancyConsoleOptions.SectionPath;
        var enabled = !string.Equals(cfg[$"{section}:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        // Report the RESOLVED posture the gate actually uses (honoring the Koan:Tenancy:Posture override), not a
        // naive env check — otherwise a dev host with Posture=Closed would announce "just-works" while the gate 403s.
        var postureOverride = Enum.TryParse<TenancyPosture>(cfg[$"{TenancyOptions.SectionPath}:Posture"], ignoreCase: true, out var p)
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
            $"{TenancyConsolePaths.UiPath} · posture={posture} · exposure={exposure} · " +
            $"operators-configured={operators} (grant = allow-list ∪ role) · audit-page={cfg[$"{section}:AuditPageSize"] ?? "100"}"));
    }
}
