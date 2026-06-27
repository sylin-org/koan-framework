using Koan.Data.Core.Axes;

namespace Koan.Data.Access;

/// <summary>
/// SEC-0008 / ARCH-0101 §7 — data-layer access scoping authored as an <see cref="IDataAxis"/>: a non-equality,
/// viewer-context read predicate (no managed-field stamp — it filters an existing entity field) plus the ambient-
/// subject async-hop carrier. Opt-in via <see cref="AccessScopedAttribute"/>; not authoring it / no opted-in entity
/// ⇒ empty fold ⇒ no-op (structural absence, Reference = Intent). Discovered at boot via <c>[KoanDiscoverable]</c> on
/// <see cref="IDataAxis"/>. <c>.Reads</c> hard-binds the axis to RowScoped + cache-exclusion (a hidden-row predicate
/// without isolation is a leak — there is no opt-out).
/// </summary>
public sealed class AccessAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("access")
        .AppliesTo(static t => AccessScopedMetadata.IsAccessScoped(t))
        .Reads(static t => AccessAmbient.ReadFilter(t))   // the framework re-applies AppliesTo=IsAccessScoped
        .Carries(new SubjectContextCarrier());
}
