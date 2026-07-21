namespace Koan.Data.Core.Routing;

/// <summary>
/// What the framework does when a <see cref="Koan.Data.Core.Axes.AxisMode.Database"/> axis routes an operation to a data
/// source that is not configured (ARCH-0102 §3 — "provisioning is a posture, not a mechanism"). The posture decides
/// <i>availability</i>; it never decides <i>placement</i> (which tenant lives where) — that is the P6 broker.
///
/// <para><b>Phase 2 realizes <see cref="ExternalOnly"/> only</b> (the fail-closed default): an unconfigured routed
/// source throws a self-explaining error (FC-7) rather than silently mis-routing. <see cref="Lazy"/> (derive/create the
/// source on first touch — the Tier-0 zero-config default) and <see cref="Eager"/> (pre-provision at boot) land with the
/// P6 broker; until then they are not selectable.</para>
/// </summary>
public enum ProvisioningPosture
{
    /// <summary>Derive and create a routed source's keyspace on first touch (zero-config Tier-0). Deferred to the P6 broker.</summary>
    Lazy,

    /// <summary>Pre-provision every routed source at boot; a missing one at resolution time is then a config bug. Deferred.</summary>
    Eager,

    /// <summary>The keyspace must already exist and be configured; routing to an absent source fails closed (the Phase-2 default).</summary>
    ExternalOnly,
}
