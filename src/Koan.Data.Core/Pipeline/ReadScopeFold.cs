using System;
using System.Collections.Generic;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The ONE read-scope fold (DATA-0106 / ARCH-0101 §9): AND-fold every registered <see cref="IReadFilterContributor"/>'s
/// predicate for an entity in the current ambient. Zero survivors ⇒ <c>null</c> (the unfiltered fast path); one ⇒ that
/// predicate; many ⇒ <c>Filter.All(survivors)</c> — no 1-element AllOf. Shared by the facade read path (then fail-closed),
/// <see cref="Axes.DataAxis.Explain"/> (the diagnostic), AND the vector search chokepoint (the
/// <c>ScopedVectorRepository</c> ANDs this fold into every KNN) — so the data read, the explanation, and the vector
/// search can never drift from one another. Public because cross-pillar consumers (the vector decorator) reuse it
/// rather than re-derive the axis scope.
/// </summary>
public static class ReadScopeFold
{
    public static Filter? Fold(IReadOnlyList<IReadFilterContributor> contributors, Type entityType)
    {
        List<Filter>? survivors = null;
        for (var i = 0; i < contributors.Count; i++)
        {
            var f = contributors[i].ReadFilter(entityType);
            if (f is null) continue;
            (survivors ??= new()).Add(f);
        }
        if (survivors is null) return null;
        return survivors.Count == 1 ? survivors[0] : Filter.All(survivors.ToArray());
    }
}
