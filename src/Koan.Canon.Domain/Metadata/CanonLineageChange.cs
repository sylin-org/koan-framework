namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Represents a tracked lineage change event.
/// </summary>
public sealed class CanonLineageChange
{
    /// <summary>
    /// Initializes a new instance of <see cref="CanonLineageChange"/>.
    /// </summary>
    public CanonLineageChange()
    {
    }

    /// <summary>
    /// Initializes a change with explicit values.
    /// </summary>
    public CanonLineageChange(CanonLineageChangeKind kind, string relatedId, DateTimeOffset occurredAt, string? notes = null)
    {
        Kind = kind;
        RelatedId = relatedId;
        OccurredAt = occurredAt;
        Notes = notes;
    }

    /// <summary>
    /// Type of lineage mutation.
    /// </summary>
    public CanonLineageChangeKind Kind { get; set; }

    /// <summary>
    /// Identifier related to the change (parent/child/replacement).
    /// </summary>
    public string RelatedId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the change.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional notes for diagnostics.
    /// </summary>
    public string? Notes { get; set; }
}
