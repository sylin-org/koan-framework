using Koan.Core;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The <b>premium authoring surface</b> for a data-segmentation axis (ARCH-0101 §7) — the discovered sugar over the
/// conformant Phase A/B/C seams. An axis author writes one type:
/// <code>
/// public sealed class TenantAxis : IDataAxis
/// {
///     public void Declare(Axis axis) => axis
///         .Named("tenant")
///         .AppliesTo(t =&gt; !IsHostScoped(t))
///         .Field("__koan_tenant", () =&gt; TenancyAmbient.EffectiveTenantId())   // stamp + auto-equality read-filter + cache-key + index + fail-closed
///         .Carries(new TenantContextCarrier());                               // ride the async-hop
/// }
/// </code>
/// <see cref="Declare"/> EXPANDS to the exact raw seams — a <c>ManagedFieldDescriptor</c>, an
/// <c>IReadFilterContributor</c>, an <c>IStorageNameParticleContributor</c>, an <c>IAmbientSliceCarrier</c>, and/or an
/// <c>OperationOverrideDescriptor</c> — so a <c>[DataAxis]</c> and the equivalent hand-written registration produce
/// <b>byte-identical behavior</b> (ARCH-0101 §7). The raw seams stay the canon a power author drops to (both/and).
///
/// <para>Marked <see cref="KoanDiscoverableAttribute"/> (the <c>IKoanJob</c> pattern): concrete implementers are
/// auto-collected into <c>KoanRegistry</c> at build/boot, and <c>DataAxisExpander</c> instantiates each
/// (<b>public parameterless ctor required</b>), calls <see cref="Declare"/>, and registers the accumulated planes.
/// The data core never names an axis; not authoring one leaves every seam empty (structural no-op, Reference = Intent).</para>
/// </summary>
[KoanDiscoverable]
public interface IDataAxis
{
    /// <summary>
    /// Declare this axis's planes onto the fluent <paramref name="axis"/> builder. Called once at boot. The builder is
    /// purely accumulative — all smart-default derivation, validation, and seam registration happen after this returns
    /// (in <c>DataAxisExpander</c>), so verb order never matters.
    /// </summary>
    void Declare(Axis axis);
}
