namespace Koan.AI.Contracts.Categories;

/// <summary>
/// Per-category configuration, bound to <c>Koan:Ai:{Category}</c>.
/// Each category (Chat, Embed, Ocr) has independent source/model routing.
/// </summary>
public sealed class AiCategoryOptions
{
    /// <summary>Default source for this category.</summary>
    public string? Source { get; set; }

    /// <summary>Default model for this category.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// For task categories: the protocol category to delegate through.
    /// Example: "Chat" for OCR via multimodal vision.
    /// </summary>
    public string? Via { get; set; }

    /// <summary>Fallback source if primary is unavailable.</summary>
    public string? Fallback { get; set; }
}
