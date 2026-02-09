using System;

namespace Koan.AI.Contracts.Categories;

/// <summary>
/// Defines an AI processing category. Categories are either protocol-level (Chat, Embed)
/// or task-level (Ocr) that delegate through a protocol category via <see cref="Via"/>.
/// </summary>
public sealed record AiCategoryDefinition
{
    /// <summary>Category name: "Chat", "Embed", "Ocr".</summary>
    public required string Name { get; init; }

    /// <summary>The adapter interface type for this category.</summary>
    public required Type AdapterInterface { get; init; }

    /// <summary>
    /// For task categories: the protocol category to delegate through when no dedicated adapter exists.
    /// Null for protocol categories (Chat, Embed).
    /// Example: Ocr has Via = "Chat" (delegates via multimodal vision request).
    /// </summary>
    public string? Via { get; init; }

    /// <summary>Default source name for this category (from config).</summary>
    public string? DefaultSource { get; init; }

    /// <summary>Default model for this category (from config).</summary>
    public string? DefaultModel { get; init; }
}
