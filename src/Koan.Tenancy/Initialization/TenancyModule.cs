using System;
using System.Collections.Generic;
using System.Linq;
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
        services.AddKoanOptions<TenancyOptions>("Koan:Data:Tenancy");
        services.TryAddSingleton<TenancyRuntime>();
        services.TryAddSingleton<TenancyDevState>();
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
        // The boot pre-flight (ARCH-0099 §1): refuse to boot for the small exploitable-absence set. It is
        // AUTHORITATIVE over the RESOLVED posture (TenancyRuntime, per-host) — not over how the posture was
        // requested — so it catches a forced-Open whether it arrived via the config key, a programmatic
        // Configure<TenancyOptions>, or any env divergence, in one invariant: Open is legal only in Development.
        var env = services.GetRequiredService<IHostEnvironment>();
        var cfg = services.GetRequiredService<IConfiguration>();
        var runtime = services.GetRequiredService<TenancyRuntime>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Tenancy");

        var result = TenancyPreflight.Evaluate(new TenancyPreflightInput(
            IsDevelopment: env.IsDevelopment(),
            IsProduction: env.IsProduction(),
            PostureIsOpen: runtime.Posture == TenancyPosture.Open,
            HasResolver: services.GetServices<ITenantResolver>().Any(),
            BrandedDevMarkerPresent: TenancyDevBrand.ContainsAny(SensitiveConfigValues(cfg))));

        foreach (var warning in result.Warnings)
            logger?.LogWarning("Tenancy pre-flight: {Warning}", warning);

        if (result.ShouldRefuseBoot)
            throw new TenancyBootException(result.HardFailures);

        logger?.LogInformation("Tenancy posture resolved: {Posture}.", runtime.Posture);

        // Dev auto-seed (ARCH-0099 §1) — only in Development with an Open posture (a forced-Open-outside-dev boot is
        // refused above, so this never runs in Staging/Production). Gating on env.IsDevelopment() too is the
        // belt-and-suspenders: a non-Development host can never seed regardless of how the posture resolved. Seed
        // one in-memory dev tenant, make the loopback caller its Owner, and mint a branded per-machine key; an
        // unset ambient scope then falls back to the dev tenant (no day-one 403).
        if (env.IsDevelopment() && runtime.Posture == TenancyPosture.Open)
        {
            var devState = services.GetRequiredService<TenancyDevState>();
            if (!devState.IsSeeded)
            {
                var seed = TenancyDevSeed.Create(cfg["Koan:Data:Tenancy:DevUser"] ?? Environment.UserName, Environment.MachineName);
                devState.Apply(seed);
                logger?.LogInformation(
                    "Tenancy dev-open: seeded tenant '{Name}' (id={Id}); you are {Role}; signing key {Key}.",
                    seed.TenantName, seed.TenantId, seed.OwnerRole, seed.SigningKey);
            }
        }

        return Task.CompletedTask;
    }

    // Every key/value under Koan:Data:Tenancy — scanned for a leaked dev brand (the dev key/secret lands here).
    private static IEnumerable<string?> SensitiveConfigValues(IConfiguration cfg)
        => cfg.GetSection("Koan:Data:Tenancy").AsEnumerable(makePathsRelative: false).Select(kv => kv.Value);

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var overrideRaw = cfg["Koan:Data:Tenancy:Posture"];
        TenancyPosture? overridePosture =
            Enum.TryParse<TenancyPosture>(overrideRaw, ignoreCase: true, out var parsed) ? parsed : null;
        var posture = TenancyPostureResolver.Resolve(env.IsDevelopment(), overridePosture);

        var source = overridePosture is null ? (env.IsDevelopment() ? "dev-open" : "closed") : "override";
        module.SetSetting("Tenancy", b => b.Value(
            $"posture={posture} ({source}); segmentation=tenant/hard; realization=pillar-owned"));
    }
}
