using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Tenancy.Initialization;

/// <summary>
/// Lights tenancy up when the <c>Koan.Tenancy</c> package is referenced (Reference = Intent, ARCH-0095): binds
/// the posture options and registers the fail-closed tenant gate as a generic <see cref="IStorageGuard"/>
/// contributor into the data-core storage pipeline (DATA-0105 §0). The data core never references this module;
/// not referencing it leaves the guard seam empty (structural no-op). Default <c>Mode=Off</c> → no-op.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Tenancy";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<TenancyOptions>("Koan:Data:Tenancy");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageGuard, TenantStorageGuard>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var mode = cfg["Koan:Data:Tenancy:Mode"] ?? "Off";
        module.AddSetting("Tenancy", $"Mode={mode}");
        module.AddSetting("Enforcement", "IStorageGuard (fail-closed chokepoint gate, ARCH-0095 P1)");
    }
}
