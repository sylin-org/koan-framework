namespace Koan.Rag.Abstractions;

/// <summary>
/// Round 1 output: what kind of content is this?
/// Produced by initial classification, consumed by strategy resolution.
/// </summary>
public sealed record ContentClassification
{
    /// <summary>
    /// Hierarchical category (e.g., "diagram/architecture", "table", "form",
    /// "chart/line", "photograph/object", "code/csharp").
    /// Slash-separated for specificity: first segment matches pre-determined
    /// strategies, full path enables fine-grained auto-generation.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Natural-language description of the content from the classification model.
    /// Used by the strategy generator for context.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Confidence score from the classifier (0.0-1.0).
    /// Low confidence may trigger a second classification attempt.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Detected language or format details (e.g., "markdown", "python", "handwritten").
    /// </summary>
    public string? FormatHint { get; init; }
}
