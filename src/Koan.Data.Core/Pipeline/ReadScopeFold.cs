using System;
using System.Collections.Generic;
using Koan.Data.Abstractions.Aodb;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The ONE read-scope composition (DATA-0106 / ARCH-0101 §9 / ARCH-0102): fold every registered
/// <see cref="IReadFilterContributor"/>'s predicate for an entity in the current ambient. Shared by the facade read
/// path (then fail-closed), <see cref="Axes.DataAxis.Explain"/> (the diagnostic), AND the vector search chokepoint —
/// so the data read, the explanation, and the vector search can never drift from one another.
///
/// <para><see cref="Compose"/> is the ARCH-0102 authority: it returns an <see cref="Aodb"/> whose elements are tagged
/// with the <see cref="FieldProvenance"/> of the managed fields each predicate references, so a plane can realize the
/// store-aware push (e.g. the vector decorator pushes only the write-stamped, non-operation-sourced elements). The
/// vector decorator's former bespoke operation-override exclusion is RELOCATED here (the FilterMentions walk, now keyed
/// on the Phase-1a descriptor provenance) — one place, not a per-plane fork. <see cref="Fold"/> is the
/// combine-all shortcut (== <see cref="Aodb.CombineAll"/>) the facade uses on its hot read path without the
/// provenance walk.</para>
/// </summary>
public static class ReadScopeFold
{
    /// <summary>
    /// AND-fold every contributor's predicate into one filter (zero ⇒ null, one ⇒ that predicate, many ⇒ Filter.All).
    /// The primary-store / facade push — every element, no provenance walk. Equivalent to <c>Compose(...).CombineAll()</c>.
    /// </summary>
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

    /// <summary>
    /// ARCH-0102 — compose the provenance-tagged <see cref="Aodb"/>: each surviving contributor predicate becomes an
    /// element whose provenance is the OR of the provenances of the managed fields it references (off ⇒ <see cref="Aodb.Empty"/>).
    /// A contributor that excludes its entity from the cache clears the cacheable bit. Consumers realize the store-aware push.
    /// </summary>
    public static Aodb Compose(IReadOnlyList<IReadFilterContributor> contributors, Type entityType)
    {
        List<AodbElement>? elements = null;
        var cacheable = true;
        var managed = ManagedFieldRegistry.ForType(entityType);   // type-memoized; the provenance source
        for (var i = 0; i < contributors.Count; i++)
        {
            var f = contributors[i].ReadFilter(entityType);
            if (f is null) continue;
            (elements ??= new()).Add(new AodbElement(f, DeriveProvenance(f, managed)));
            if (contributors[i].ExcludesFromCache(entityType)) cacheable = false;
        }
        return elements is null ? Aodb.Empty : new Aodb(elements, cacheable);
    }

    // The element's provenance = OR of the provenances of the managed fields its predicate references (ARCH-0102 §3).
    // A predicate over no managed field defaults to AmbientStamped (a plain equality / user-shaped scope).
    private static FieldProvenance DeriveProvenance(Filter f, IReadOnlyList<ManagedFieldDescriptor> managed)
    {
        var prov = FieldProvenance.None;
        Collect(f, managed, ref prov);
        return prov == FieldProvenance.None ? FieldProvenance.AmbientStamped : prov;
    }

    private static void Collect(Filter f, IReadOnlyList<ManagedFieldDescriptor> managed, ref FieldProvenance prov)
    {
        switch (f)
        {
            case FieldFilter ff:
                for (var i = 0; i < managed.Count; i++)
                    if (string.Equals(managed[i].StorageName, ff.Field.Leaf, StringComparison.Ordinal))
                        prov |= managed[i].Provenance;
                break;
            case AllOf a: foreach (var op in a.Operands) Collect(op, managed, ref prov); break;
            case AnyOf a: foreach (var op in a.Operands) Collect(op, managed, ref prov); break;
            case Not n: Collect(n.Operand, managed, ref prov); break;
        }
    }
}
