using System;
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
