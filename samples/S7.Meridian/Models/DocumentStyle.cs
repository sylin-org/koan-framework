using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Defines document style classifications for extraction strategy selection.
/// Each style has detection hints and influences how facts are extracted from documents.
/// </summary>
public sealed class DocumentStyle : Entity<DocumentStyle>
{
    /// <summary>Display name for this document style (e.g., "Sparse Form", "Dialogue").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short code for this style (e.g., "SPARSE", "DIALOGUE", "NARRATIVE").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Description of this document style and when it applies.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Version of this document style definition.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Categorization tags for discovery and filtering.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Detection hints: indicators that suggest this document style.
    /// Used by LLM classifier to identify document style.
    /// Example: "Repetitive 'Question X:' patterns", "Email headers", "Multiple speakers"
    /// </summary>
    public List<string> DetectionHints { get; set; } = new();

    /// <summary>
    /// Signal phrases: specific text patterns that strongly indicate this style.
    /// Example: "From:", "To:", "Subject:", "Attendees:", "Request Type options include"
    /// </summary>
    public List<string> SignalPhrases { get; set; } = new();

    /// <summary>
    /// Extraction strategy instructions for this document style.
    /// Describes how the classifier should approach fact extraction for this style.
    /// </summary>
    public string ExtractionStrategy { get; set; } = string.Empty;

    /// <summary>Whether this style should use RAG-based passage retrieval before extraction.</summary>
    public bool UsePassageRetrieval { get; set; } = false;

    /// <summary>Number of top-K passages to retrieve if UsePassageRetrieval is true.</summary>
    public int PassageRetrievalTopK { get; set; } = 5;

    /// <summary>Whether to expand passages with surrounding context (useful for dialogues).</summary>
    public bool ExpandPassageContext { get; set; } = false;

    /// <summary>Context window size for passage expansion (before/after).</summary>
    public int ContextWindowSize { get; set; } = 2;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
