using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Abstractions.Aodb;

/// <summary>
/// ARCH-0102 — the composed <b>Access Overlay Definition Block</b> for a read: the ordered, provenance-tagged set of
/// read-scope elements an operation must hold. The framework composes it once (the contributor fold) and each plane
/// realizes the slice it can keep current — the <b>store-aware push</b>:
/// <list type="bullet">
/// <item><see cref="CombineAll"/> — every element (the primary store runs every operation, so it keeps them all current).</item>
/// <item><see cref="CombineWriteStamped"/> — only ambient-stamped, non-operation-sourced elements (a secondary store that
/// write-stamps but never runs the operation, e.g. an independent vector index, cannot keep an operation-sourced field
/// current — pushing its predicate would over-filter or vacuously leak, the soft-delete-on-vector class).</item>
/// </list>
/// A first-class, inspectable artifact (ARCH-0102 Addendum II) — the same object the diagnostic (<c>.Explain</c>) renders.
/// Off (no contributor scopes the type) ⇒ <see cref="Empty"/> ⇒ a null combine ⇒ byte-identical no-op.
/// </summary>
public sealed record Aodb(IReadOnlyList<AodbElement> Elements, bool Cacheable)
{
    /// <summary>The empty overlay — nothing is scoped (cacheable by default; no exclusion).</summary>
    public static readonly Aodb Empty = new(Array.Empty<AodbElement>(), true);

    public bool IsEmpty => Elements.Count == 0;

    /// <summary>The full read-scope predicate — every element AND-folded (the primary-store push). Null ⇒ unscoped.</summary>
    public Filter? CombineAll() => Combine(Elements);

    /// <summary>
    /// The read-scope a write-stamp-only store can enforce: elements that are ambient-stamped AND not operation-sourced
    /// (so the field is materialised on every write and never goes stale on an operation the store didn't run). Null ⇒ unscoped.
    /// </summary>
    public Filter? CombineWriteStamped()
        => Combine(Elements.Where(static e =>
            (e.Provenance & FieldProvenance.AmbientStamped) != 0 &&
            (e.Provenance & FieldProvenance.OperationSourced) == 0));

    private static Filter? Combine(IEnumerable<AodbElement> elements)
    {
        List<Filter>? preds = null;
        foreach (var e in elements) (preds ??= new()).Add(e.Predicate);
        if (preds is null) return null;
        return preds.Count == 1 ? preds[0] : Filter.All(preds.ToArray());
    }
}
