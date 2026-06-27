using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.Data.Access.Initialization;

/// <summary>
/// SEC-0008 — lights up data-layer access scoping when <c>Koan.Data.Access</c> is referenced (Reference = Intent). The
/// read-filter contributor + the subject async-hop carrier are declared in <see cref="AccessAxis"/> (a
/// <c>[KoanDiscoverable]</c> <c>IDataAxis</c>) and expanded byte-identically by <c>DataAxisExpander</c> at boot. This
/// registrar binds <see cref="AccessOptions"/> and serves as the boot-report marker. The per-entity opt-in is
/// <see cref="AccessScopedAttribute"/>.
/// </summary>
public sealed class KoanAutoRegistrar : KoanModule
{
    public override string Id => "Koan.Data.Access";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AccessOptions>(AccessOptions.SectionPath);
    }
}
