using Koan.Data.Core.Axes;

namespace Koan.Data.Access;

/// <summary>
/// Data-owned access scoping: a non-equality, viewer-context read predicate over an existing entity field. Durable
/// subject carriage is registered independently through Core by the module registrar; it is not a Data-axis plane.
/// <c>.Reads</c> hard-binds the axis to RowScoped plus cache exclusion because a hidden-row predicate without
/// isolation is a leak.
/// </summary>
public sealed class AccessAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("access")
        .AppliesTo(static t => AccessScopedMetadata.IsAccessScoped(t))
        .Reads(static t => AccessAmbient.ReadFilter(t));   // the framework re-applies AppliesTo=IsAccessScoped
}
