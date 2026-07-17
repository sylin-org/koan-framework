using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Modules;

namespace Koan.Data.Access.Initialization;

/// <summary>
/// Lights up data-layer access scoping when <c>Koan.Data.Access</c> is referenced. The module binds policy and
/// independently registers subject context carriage through Core; <see cref="AccessAxis"/> owns only the Data read
/// plane. The per-entity opt-in is <see cref="AccessScopedAttribute"/>.
/// </summary>
public sealed class DataAccessModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AccessOptions>(AccessOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanContextCarrier, SubjectContextCarrier>());
    }
}
