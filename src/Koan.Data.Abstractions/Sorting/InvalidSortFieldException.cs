namespace Koan.Data.Abstractions.Sorting;

/// <summary>
/// Thrown when a sort field string cannot be resolved against the target entity type.
/// Surfaces as <c>400 Bad Request</c> in the web layer (unless lenient mode is enabled).
/// </summary>
public sealed class InvalidSortFieldException : ArgumentException
{
    /// <summary>The raw sort field string that failed to resolve.</summary>
    public string Field { get; }

    /// <summary>The entity type the field was being resolved against.</summary>
    public Type EntityType { get; }

    /// <summary>The first segment of the dot-path that failed to resolve (may equal Field when no dots are present).</summary>
    public string FailedSegment { get; }

    public InvalidSortFieldException(string field, Type entityType, string failedSegment)
        : base(BuildMessage(field, entityType, failedSegment))
    {
        Field = field;
        EntityType = entityType;
        FailedSegment = failedSegment;
    }

    public InvalidSortFieldException(string field, Type entityType, string failedSegment, string detail)
        : base(BuildMessage(field, entityType, failedSegment) + " " + detail)
    {
        Field = field;
        EntityType = entityType;
        FailedSegment = failedSegment;
    }

    private static string BuildMessage(string field, Type entityType, string failedSegment)
        => $"Sort field '{field}' cannot be resolved on type '{entityType.Name}' at segment '{failedSegment}'.";
}
