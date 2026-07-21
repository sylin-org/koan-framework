using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// ARCH-0102 §5 — a vector adapter that DECLARES an overlay-naming rule because its store cannot accept the
/// default <c>__</c> marker (e.g. Weaviate queries over GraphQL, which reserves a leading <c>__</c>). The
/// framework reads this once and applies the rule at write-stamp and read-filter alike, so an injected
/// isolation discriminator (<c>__koan_tenant</c>, a future axis's <c>__*</c>) survives the store with the same
/// spelling on both paths. Adapters whose store accepts <c>__</c> do not implement this — <c>null</c> is the default.
/// </summary>
public interface IOverlayNamingAware
{
    /// <summary>The declared overlay-naming rule, or <c>null</c> for the default (no rename).</summary>
    OverlayNamingRule? OverlayNaming { get; }
}
