using Koan.Data.Core.Model;
using Newtonsoft.Json;

namespace Koan.Samples.Meridian.Models;

public sealed class ExtractedField : Entity<ExtractedField>
{
    public string PipelineId { get; set; } = string.Empty;
    public string FieldPath { get; set; } = string.Empty;

    public string? ValueJson { get; set; }
        = null;

    public double Confidence { get; set; }
        = 0.0;

    public string? SourceDocumentId { get; set; }
        = null;

    public string? PassageId { get; set; }
        = null;

    /// <summary>Source type of this extracted field value.</summary>
    public FieldSource Source { get; set; }
        = FieldSource.DocumentExtraction;

    /// <summary>Precedence level for merge conflicts (lower = higher priority).</summary>
    public int Precedence { get; set; }
        = 10;

    public TextSpanEvidence Evidence { get; set; } = new();

    public bool UserApproved { get; set; }
        = false;
    public string? ApprovedBy { get; set; }
        = null;
    public DateTime? ApprovedAt { get; set; }
        = null;

    public bool Overridden { get; set; }
        = false;
    public string? OverrideValueJson { get; set; }
        = null;
    public string? OverrideReason { get; set; }
        = null;
    public string? OverriddenBy { get; set; }
        = null;
    public DateTime? OverriddenAt { get; set; }
        = null;

    public string? MergeStrategy { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasEvidenceText()
        => !string.IsNullOrWhiteSpace(Evidence.OriginalText);
}

public sealed class TextSpanEvidence
{
    public string? PassageId { get; set; }
        = null;

    public string? SourceDocumentId { get; set; }
        = null;

    public string OriginalText { get; set; } = string.Empty;

    public TextSpan? Span { get; set; }
        = null;

    public int? Page { get; set; }
        = null;

    public string? Section { get; set; }
        = null;

    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public string ToJson()
        => JsonConvert.SerializeObject(this);
}

public sealed class TextSpan
{
    public int Start { get; set; }
        = 0;
    public int End { get; set; }
        = 0;
}

/// <summary>
/// Indicates the source of an extracted field value for precedence-based merging.
/// Lower enum values indicate higher priority in conflict resolution.
/// </summary>
public enum FieldSource
{
    /// <summary>Value from Authoritative Notes (highest priority, precedence=1).</summary>
    AuthoritativeNotes = 1,

    /// <summary>Manual override by user during review (precedence=2).</summary>
    ManualOverride = 2,

    /// <summary>AI-extracted from uploaded documents (precedence=3+).</summary>
    DocumentExtraction = 3,

    /// <summary>No value found in any source (precedence=999).</summary>
    NotFound = 999
}
