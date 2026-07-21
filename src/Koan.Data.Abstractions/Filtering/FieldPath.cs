namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// A reflection-neutral path to an entity field: one or more segments (e.g.
/// <c>["Address", "City"]</c> for a nested member). The AST carries only the segments so it
/// stays serializable and provider-independent; resolution to a concrete CLR member and
/// leaf type is the job of <c>FieldPathResolver</c>, keeping the predicate model free of
/// reflection concerns (separation of concerns).
/// </summary>
public sealed record FieldPath(IReadOnlyList<string> Segments, Type? ManagedClrType = null)
{
    public static FieldPath Of(params string[] segments) => new(segments);

    /// <summary>Creates a framework-managed storage leaf without pretending it is a CLR property.</summary>
    public static FieldPath Managed(string storageName, Type clrType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);
        ArgumentNullException.ThrowIfNull(clrType);
        return new FieldPath([storageName.Trim()], clrType);
    }

    /// <summary>The final segment — the leaf member being compared.</summary>
    public string Leaf => Segments[Segments.Count - 1];

    public override string ToString() => string.Join('.', Segments);
}
