namespace Koan.Data.Abstractions.Sorting;

/// <summary>
/// Structured sort specification carrying a resolved <see cref="MemberPath"/>, direction, and
/// optional aggregation hint for collection-traversing paths.
/// </summary>
/// <param name="Path">Resolved member chain against the entity root type.</param>
/// <param name="Desc">True for descending order.</param>
/// <param name="Aggregation">How to aggregate when Path traverses a collection; None for scalar paths.</param>
public sealed record SortSpec(MemberPath Path, bool Desc, SortAggregation Aggregation = SortAggregation.None)
{
    /// <summary>Canonical string form (e.g. "-Sightings.LastChangedAt"). Useful for logging and diagnostics.</summary>
    public string ToCanonicalString()
        => (Desc ? "-" : "+") + Path.DotPath;
}
