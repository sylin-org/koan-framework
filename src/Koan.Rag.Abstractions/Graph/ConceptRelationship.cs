namespace Koan.Rag.Abstractions;

/// <summary>
/// A relationship between two concept entities, extracted from document content.
/// Used only with <see cref="GraphStrategy.Full"/>; the Lightweight strategy
/// uses embedding proximity instead of explicit relationships.
/// </summary>
public sealed record ConceptRelationship
{
    /// <summary>Stable unique identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Source entity ID.</summary>
    public required string FromEntityId { get; init; }

    /// <summary>Target entity ID.</summary>
    public required string ToEntityId { get; init; }

    /// <summary>
    /// Natural-language relationship label (e.g., "requires", "is-a",
    /// "governed-by", "contains", "was-invented-by").
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Confidence score from the extraction LLM (0.0 to 1.0).
    /// Relationships below the configurable threshold are not persisted.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Source document that evidenced this relationship.</summary>
    public string? SourceDocumentId { get; init; }
}
