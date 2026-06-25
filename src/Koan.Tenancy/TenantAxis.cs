using Koan.Data.Core.Axes;

namespace Koan.Tenancy;

/// <summary>
/// ARCH-0102 / ARCH-0101 §7 — tenancy's data-axis planes authored as a <see cref="IDataAxis"/> (the golden authoring
/// shrink): the invisible <c>__koan_tenant</c> managed field and the durable async-hop carrier are declared in ONE
/// <see cref="Declare"/> body, replacing the hand registrations. <b>Byte-identical</b> — Shared mode derives the same
/// RowScoped + indexed + auto-equality (tenant equality) descriptor, and the same <see cref="TenantContextCarrier"/>
/// (ARCH-0100). The <see cref="IStorageGuard"/> gate, the posture pre-flight, the dev-seed, and the boot report stay in
/// the registrar — they are <i>policy</i>, not an axis plane. Discovered at boot via <c>[KoanDiscoverable]</c> on
/// <see cref="IDataAxis"/>. No tenant in scope ⇒ the value provider returns <c>null</c> ⇒ inert (byte-identical).
/// </summary>
public sealed class TenantAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("tenant")
        .AppliesTo(static t => !TenantScopeMetadata.IsHostScopedType(t))
        .Field("__koan_tenant", static () => TenancyAmbient.EffectiveTenantId(), typeof(string))
        .Carries(new TenantContextCarrier());
}
