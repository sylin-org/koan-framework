namespace Koan.Data.Core.Axes;

/// <summary>
/// How a data-segmentation axis manifests (ARCH-0101 §7 — "mode is config"): the <i>same</i> axis declaration maps to
/// a different composition plane depending on the mode. Mode selects the persistence plane; durable logical-flow
/// carriage is registered independently through Core context carriers.
/// </summary>
public enum AxisMode
{
    /// <summary>
    /// The default — a <b>shared store</b> segmented by a managed record field + read-filter. <c>.Field</c> registers
    /// the managed column (stamp + serialize + index + cache-key partition); the read scope is the built-in
    /// auto-equality fold (tenancy/classification) or a non-equality <c>.Reads</c> predicate (soft-delete/moderation).
    /// </summary>
    Shared = 0,

    /// <summary>
    /// A <b>separate physical container</b> per axis value (ARCH-0101 §3, e.g. <c>T1-Todo</c>). <c>.Field</c>'s value
    /// becomes a leading storage-name particle (<c>IStorageNameParticleContributor</c>) — the anchor is untouched,
    /// "the axis is never in the spine". No managed column, no read-filter (the container IS the isolation).
    /// </summary>
    Container = 1,

    /// <summary>
    /// A <b>separate data source</b> per axis value (DATA-0077 source routing, ARCH-0102 §3 auto-routing). <c>.Field</c>'s
    /// value provider is the per-operation SOURCE-KEY provider — its value (read from the ambient) selects the data source
    /// the framework routes to; the expander registers it as a <c>DatabaseRouteDescriptor</c> that <c>AdapterResolver</c>
    /// consults (after an explicit <c>EntityContext.Source</c>, which always wins). A module that crosses a durable hop
    /// registers its Core context carrier independently. No managed column, no read-filter (the separate
    /// source IS the isolation); declaring <c>.Reads</c>/<c>.OnDelete</c> is rejected. The unconfigured-source posture is
    /// external-only (fail closed) until lazy provisioning lands (the P6 broker).
    /// </summary>
    Database = 2,
}
