namespace Koan.Data.Core.Axes;

/// <summary>
/// How a data-segmentation axis manifests (ARCH-0101 §7 — "mode is config"): the <i>same</i> axis declaration maps to
/// a different composition plane depending on the mode. Mode selects the plane; the axis value source (<c>.Field</c> /
/// <c>.Carries</c>) is mode-agnostic.
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
    /// A <b>separate data source</b> per axis value (DATA-0077 ambient source routing). There is no contributor plane —
    /// routing rides <c>EntityContext.With(source:)</c>, driven by the axis's own <c>.Carries</c> scope. The expander
    /// registers only the carrier; declaring <c>.Field</c>/<c>.Reads</c>/<c>.OnDelete</c> in this mode is rejected.
    /// </summary>
    Database = 2,
}
