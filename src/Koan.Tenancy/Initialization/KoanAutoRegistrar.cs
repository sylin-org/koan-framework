using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
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
/// fail-closed tenant gate as a generic <see cref="IStorageGuard"/> contributor, and registers the invisible
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageGuard, TenantStorageGuard>());

        // The tenant managed field — the invisible shadow discriminator (no POCO property). The framework stamps
        // it on every write inside a tenant scope and AND-folds it into reads; on an adapter that announces
        // isolation (DataCaps.Isolation.RowScoped) it isolates, otherwise a tenant-scoped op fails closed. Inert
        // (value null) outside a tenant scope, so [HostScoped] entities and unscoped writes are unaffected.
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
            StorageName: "__koan_tenant",
            ClrType: typeof(string),
            ValueProvider: static () => Tenant.Current?.Id,
            AppliesTo: static t => !TenantScopeMetadata.IsHostScopedType(t),
            RequiredCapability: DataCaps.Isolation.RowScoped,
            Indexed: true));
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        // The boot pre-flight (ARCH-0099 §1): refuse to boot in Production for the small exploitable-absence set
        // (no resolver / a dev-branded artifact / a forced dev-open posture); warn softly otherwise. The prod
        // signal comes from the per-host IHostEnvironment (the env abstraction KoanEnv itself wraps) so the check
        // is per-host correct and testable; the resolved posture (TenancyRuntime) stays KoanEnv-derived.
        var env = services.GetRequiredService<IHostEnvironment>();
        var cfg = services.GetRequiredService<IConfiguration>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Tenancy");

        var overrideRequestedOpen = string.Equals(
            cfg["Koan:Data:Tenancy:Posture"], nameof(TenancyPosture.Open), StringComparison.OrdinalIgnoreCase);
        var hasResolver = services.GetServices<ITenantResolver>().Any();
        var brandedPresent = TenancyDevBrand.ContainsAny(SensitiveConfigValues(cfg));

        var result = TenancyPreflight.Evaluate(new TenancyPreflightInput(
            IsProduction: env.IsProduction(),
            OverrideRequestedOpen: overrideRequestedOpen,
            HasResolver: hasResolver,
            BrandedDevMarkerPresent: brandedPresent));

        foreach (var warning in result.Warnings)
            logger?.LogWarning("Tenancy pre-flight: {Warning}", warning);

        if (result.ShouldRefuseBoot)
            throw new TenancyBootException(result.HardFailures);

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
