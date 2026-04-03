namespace Koan.Rag.Abstractions;

/// <summary>
/// Output from a content adapter: the fully interpreted text content
/// ready for chunking, embedding, and entity extraction.
/// </summary>
public sealed record ContentExtractionResult
{
    /// <summary>
    /// Primary extracted text — the full interpretation of the content.
    /// This is what gets chunked and embedded.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Structured sections within the content (if the adapter detected
    /// document structure). Empty for single-section content.
    /// </summary>
    public IReadOnlyList<ContentSection> Sections { get; init; } = [];

    /// <summary>Classification produced by Round 1.</summary>
    public ContentClassification? Classification { get; init; }

    /// <summary>The interpretation strategy that was used.</summary>
    public string? StrategyUsed { get; init; }

    /// <summary>Number of interpretation rounds executed.</summary>
    public int RoundsExecuted { get; init; } = 1;

    /// <summary>
    /// Metadata extracted during interpretation (e.g., table headers,
    /// diagram component list, form field names).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>Empty result for content that couldn't be processed.</summary>
    public static ContentExtractionResult Empty { get; } = new() { Text = "" };
}

/// <summary>
/// A structural section within extracted content.
/// </summary>
public sealed record ContentSection(
    string? Title,
    string Text,
    int TokenEstimate);
