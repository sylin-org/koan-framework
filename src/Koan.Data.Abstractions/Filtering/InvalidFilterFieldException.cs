namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Thrown when a <see cref="FieldPath"/> cannot be resolved against the target entity type.
/// Surfaces as <c>400 Bad Request</c> in the web layer (parallel to the sort path's
/// <c>InvalidSortFieldException</c>; the two converge in a later phase).
/// </summary>
public sealed class InvalidFilterFieldException : ArgumentException
{
    /// <summary>The raw field path that failed to resolve.</summary>
    public string Field { get; }

    /// <summary>The entity type the field was being resolved against.</summary>
    public Type EntityType { get; }

    /// <summary>The first path segment that failed to resolve.</summary>
    public string FailedSegment { get; }

    public InvalidFilterFieldException(string field, Type entityType, string failedSegment)
        : base(BuildMessage(field, entityType, failedSegment))
    {
        Field = field;
        EntityType = entityType;
        FailedSegment = failedSegment;
    }

    public InvalidFilterFieldException(string field, Type entityType, string failedSegment, string detail)
        : base(BuildMessage(field, entityType, failedSegment) + " " + detail)
    {
        Field = field;
        EntityType = entityType;
        FailedSegment = failedSegment;
    }

    private static string BuildMessage(string field, Type entityType, string failedSegment)
        => $"Filter field '{field}' cannot be resolved on type '{entityType.Name}' at segment '{failedSegment}'.";
}
