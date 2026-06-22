using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Tenancy.Initialization;

/// <summary>
/// Lights tenancy up when the <c>Koan.Tenancy</c> package is referenced (Reference = Intent, ARCH-0095): binds
/// the posture options, registers the fail-closed tenant gate as a generic <see cref="IStorageGuard"/>
/// contributor, and registers the invisible <c>__koan_tenant</c> <see cref="ManagedFieldDescriptor"/> the data
/// core stamps on writes and filters on reads (DATA-0105 §0/§3b). The data core never references this module;
/// not referencing it leaves both seams empty (structural no-op). Default <c>Mode=Off</c> / no tenant in scope →
/// the descriptor is inert (its value provider returns <c>null</c>), so a non-tenant app is byte-identical.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Tenancy";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyOptions>("Koan:Data:Tenancy");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageGuard, TenantStorageGuard>());

        // The tenant managed field — the invisible shadow discriminator (no POCO property). The framework stamps
        // it on every write inside a tenant scope and AND-folds it into reads; on an adapter that announces
        // isolation (DataCaps.Isolation.RowScoped) it isolates, otherwise a tenant-scoped op fails closed. Inert
        // (value null) outside a tenant scope, so [HostScoped] entities and unscoped/Off apps are unaffected.
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
            StorageName: "__koan_tenant",
            ClrType: typeof(string),
            ValueProvider: static () => Tenant.Current?.Id,
            AppliesTo: static t => !TenantScopeMetadata.IsHostScopedType(t),
            RequiredCapability: DataCaps.Isolation.RowScoped,
            Indexed: true));
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var mode = cfg["Koan:Data:Tenancy:Mode"] ?? "Off";
        module.AddSetting("Tenancy", $"Mode={mode}");
        module.AddSetting("Enforcement", "IStorageGuard (fail-closed chokepoint gate, ARCH-0095 P1)");
    }
}
