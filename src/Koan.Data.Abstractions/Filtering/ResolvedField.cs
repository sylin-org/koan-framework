using System.Reflection;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// A <see cref="FieldPath"/> resolved against a concrete entity type: the CLR member chain
/// plus the type facts translators and the evaluator need (leaf type, whether the leaf is a
/// collection, the element/comparable type used to coerce filter values). Produced by
/// <see cref="FieldPathResolver"/>; the AST itself stays reflection-free.
/// </summary>
public sealed record ResolvedField(
    Type RootType,
    IReadOnlyList<MemberInfo> Members,
    Type LeafType,
    Type ComparableType,
    bool TargetsCollection,
    Type? ElementType,
    bool IsManaged = false,
    string? StorageName = null,
    FieldPath? CanonicalPath = null)
{
    /// <summary>Reads the leaf value from an entity instance, walking the member chain. Null-safe.</summary>
    public object? GetValue(object? entity)
    {
        // A managed field (DATA-0105 §3b, Seam 3) has NO CLR member chain — its value lives in the persisted
        // record, not the typed entity. Walking the (empty) chain would return the entity itself, making an
        // in-memory residual evaluate Eq(entity, value) ≡ false → a silent-empty isolation break. A managed
        // predicate must therefore never land as a residual (the chokepoint asserts it is pushed down); this
        // throw is the fail-loud belt-and-suspenders so a residual eval can never silently return a wrong value.
        if (IsManaged)
            throw new InvalidOperationException(
                $"Managed field '{StorageName}' cannot be evaluated in memory — it must be pushed down. A managed " +
                "predicate landed as a residual, which the chokepoint capability gate is supposed to forbid.");

        var current = entity;
        foreach (var member in Members)
        {
            if (current is null) return null;
            current = member switch
            {
                PropertyInfo p => p.GetValue(current),
                FieldInfo f => f.GetValue(current),
                _ => null
            };
        }
        return current;
    }
}
