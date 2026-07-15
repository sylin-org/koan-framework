using Koan.Data.Core.Axes;

namespace Koan.Tenancy;

/// <summary>
/// Tenancy's Data-owned axis declaration: the invisible <c>__koan_tenant</c> managed field, applicability, and
/// equality isolation policy. Durable context carriage is registered independently through Core by the Tenancy
/// module registrar; it is not a Data-axis plane.
/// </summary>
public sealed class TenantAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("tenant")
        .AppliesTo(static t => !TenantScopeMetadata.IsHostScopedType(t))
        .Field("__koan_tenant", static () => TenancyAmbient.EffectiveTenantId(), typeof(string));
}
