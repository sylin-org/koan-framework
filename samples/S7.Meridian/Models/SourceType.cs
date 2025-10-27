using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class SourceType : Entity<SourceType>
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Exclusive short code for this source type (e.g., "MEET", "INV", "CONT").</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    public List<string> Tags { get; set; } = new();
    public List<string> DescriptorHints { get; set; } = new();
    public List<string> SignalPhrases { get; set; } = new();
    public bool SupportsManualSelection { get; set; } = true;

    /// <summary>
    /// If true, documents classified with this type will be marked as processed but skipped from chunking, indexing, and extraction.
    /// Used for test files or non-document content.
    /// </summary>
    public bool SkipProcessing { get; set; } = false;
    public int? ExpectedPageCountMin { get; set; }
        = null;
    public int? ExpectedPageCountMax { get; set; }
        = null;
    public List<string> MimeTypes { get; set; } = new();
    public Dictionary<string, string> FieldQueries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Instructions { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = string.Empty;

    public float[]? TypeEmbedding { get; set; }
        = null;
    public int TypeEmbeddingVersion { get; set; }
        = 0;
    public string? TypeEmbeddingHash { get; set; }
        = null;
    public DateTime? TypeEmbeddingComputedAt { get; set; }
        = null;

    public DateTime? InstructionsUpdatedAt { get; set; }
        = null;
    public DateTime? OutputTemplateUpdatedAt { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
