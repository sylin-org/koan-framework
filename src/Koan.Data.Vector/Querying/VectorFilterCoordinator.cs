using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Querying;

/// <summary>
/// The single negotiation/enforcement point for vector metadata filtering (AI-0036 §10 /
/// DATA-0097 P1). It sits at the read boundary inside <c>VectorData&lt;T&gt;.Search</c> — below every
/// <c>Vector&lt;T&gt;</c> facade overload and above all provider repos — so the residual-is-error
/// invariant cannot be bypassed by any current or future facade.
/// </summary>
/// <remarks>
/// It runs the shared <see cref="FilterSplitter"/> split against the adapter's
/// <see cref="VectorFilterCapabilities"/> and, unlike the entity <c>FilterPushdownCoordinator</c>,
/// treats <b>any non-empty residual as a hard error</b> (<see cref="VectorFilterUnsupportedException"/>).
/// Vector search has no in-memory floor: evaluating a residual after the kNN top-K would silently
/// drop matching rows that the truncated candidate set never contained (DATA-0097 §3). The repo
/// receives only a fully-pushable <see cref="Filter"/> and never re-parses or re-negotiates.
/// </remarks>
public static class VectorFilterCoordinator
{
    /// <summary>
    /// Validates that <paramref name="filter"/> is fully pushable under <paramref name="capabilities"/>
    /// and returns the pushable tree (identical to the input when fully pushable; <c>null</c> when
    /// there is no filter). Throws <see cref="VectorFilterUnsupportedException"/> if any clause cannot
    /// be pushed.
    /// </summary>
    public static Filter? Validate(Filter? filter, VectorFilterCapabilities capabilities, string provider)
    {
        if (filter is null) return null;

        var split = FilterSplitter.Split(filter, capabilities);
        if (split.Residual is not null)
            throw VectorFilterUnsupportedException.ForResidual(provider, split.Residual);

        return split.Pushable;
    }
}
