using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Tenancy.Initialization;

/// <summary>
/// Lights tenancy up when the <c>Koan.Tenancy</c> package is referenced (Reference = Intent, ARCH-0099 §1): binds
/// the posture options, resolves the runtime posture once (<see cref="TenancyRuntime"/>), registers the
/// fail-closed tenant gate as a generic <see cref="IStorageGuard"/> contributor, registers durable tenant context
/// carriage through Core, and declares the invisible
/// <c>__koan_tenant</c> <see cref="ManagedFieldDescriptor"/> the data core stamps on writes and filters on reads
/// (DATA-0105 §0/§3b). The data core never references this module; not referencing it leaves both seams empty
/// (structural no-op). No tenant in scope → the descriptor is inert (its value provider returns <c>null</c>), so
/// a non-tenant write is byte-identical. There is no <c>Off</c> state — the posture (dev-open / prod-closed),
/// not a flag, decides how strict the gate is (ARCH-0099 §1).
/// </summary>
public sealed class KoanAutoRegistrar : KoanModule
{
    public override string Id => "Koan.Tenancy";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyOptions>("Koan:Data:Tenancy");
        services.TryAddSingleton<TenancyRuntime>();
        services.TryAddSingleton<TenancyDevState>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageGuard, TenantStorageGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanContextCarrier, TenantContextCarrier>());

        // TenantAxis owns only Data behavior. Durable carriage is a separate Core concern and remains available to
        // Jobs or Communication without making either mechanism part of the Data-axis DSL.
    }

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
            $"posture={posture} ({source}); enforcement=IStorageGuard chokepoint (ARCH-0095 P1)"));
    }
}
