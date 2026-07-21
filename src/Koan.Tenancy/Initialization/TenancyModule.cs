using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Contributions;
using Koan.Core.Semantics.Segmentation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Tenancy.Infrastructure;

namespace Koan.Tenancy.Initialization;

/// <summary>
/// Lights tenancy up when the <c>Koan.Tenancy</c> package is referenced (Reference = Intent, ARCH-0099 §1): binds
/// the posture options, resolves the runtime posture once (<see cref="TenancyRuntime"/>), registers durable tenant
/// context carriage through Core, and contributes one hard <c>tenant</c> segmentation dimension. Active pillars
/// compile their own physical realization; Tenancy does not register Data, Cache, or Storage mechanics. Not
/// referencing this module leaves the family plan empty (structural no-op). There is no <c>Off</c> state — the
/// posture (dev-open / prod-closed), not a flag, decides how an otherwise-missing tenant is resolved.
/// </summary>
public sealed class TenancyModule : KoanModule, IContributeTo<SegmentationContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyOptions>(TenancyOptions.SectionPath);
        services.TryAddSingleton<TenancyRuntime>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanContextCarrier, TenantContextCarrier>());

        // Durable carriage is a separate Core concern. The segmentation contribution below declares tenant meaning
        // once; active pillars compile their own physical realization without Tenancy referencing them.
    }

    public void Contribute(SegmentationContributionTarget target) => target.Require(
        Constants.Segmentation.DimensionId,
        static () =>
        {
            if (Tenant.Current is { IsHost: true }) return SegmentationValue.Host;
            return TenancyAmbient.EffectiveTenantId() is { Length: > 0 } tenantId
                ? SegmentationValue.For(tenantId)
                : SegmentationValue.Missing;
        },
        static type => !TenantScopeMetadata.IsHostScopedType(type),
        Constants.Segmentation.Correction);

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        var runtime = services.GetRequiredService<TenancyRuntime>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Tenancy");

        if (runtime.Posture == TenancyPosture.Open && !env.IsDevelopment())
            throw new InvalidOperationException(
                "Tenancy posture resolved to Open outside Development. Remove the " +
                $"{TenancyOptions.SectionPath}:Posture=Open override or run the host in Development.");

        if (runtime.Posture == TenancyPosture.Open)
            logger?.LogInformation(
                "Tenancy posture resolved: Open; unscoped operations use local tenant '{TenantId}'.",
                Constants.Development.TenantId);
        else
            logger?.LogInformation("Tenancy posture resolved: Closed; unscoped tenant operations fail closed.");

        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var overrideRaw = cfg[$"{TenancyOptions.SectionPath}:Posture"];
        TenancyPosture? overridePosture =
            Enum.TryParse<TenancyPosture>(overrideRaw, ignoreCase: true, out var parsed) ? parsed : null;
        var posture = TenancyPostureResolver.Resolve(env.IsDevelopment(), overridePosture);

        var source = overridePosture is null ? (env.IsDevelopment() ? "dev-open" : "closed") : "override";
        module.SetSetting("Tenancy", b => b.Value(
            $"posture={posture} ({source}); segmentation=tenant/hard; realization=pillar-owned"));
    }
}
