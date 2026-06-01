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
    Type? ElementType)
{
    /// <summary>Reads the leaf value from an entity instance, walking the member chain. Null-safe.</summary>
    public object? GetValue(object? entity)
    {
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
