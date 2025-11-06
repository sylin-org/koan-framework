using Koan.Data.Core.Model;
using Koan.Samples.Meridian.Infrastructure;

namespace Koan.Samples.Meridian.Models;

public sealed class SourceDocument : Entity<SourceDocument>
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? MediaType { get; set; }
        = null;
    public long Size { get; set; }
        = 0;
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if this is a virtual document (created from Authoritative Notes)
    /// rather than a real uploaded file.
    /// </summary>
    public bool IsVirtual { get; set; }
        = false;

    /// <summary>
    /// Precedence level for merge conflicts (lower = higher priority).
    /// Virtual documents from Authoritative Notes have precedence=1.
    /// Regular documents have precedence=10+.
    /// </summary>
    public int Precedence { get; set; }
        = 10;

    /// <summary>Classification of the source document (e.g., AuditedFinancial, VendorPrescreen).</summary>
    public string SourceType { get; set; } = MeridianConstants.SourceTypes.Unspecified;

    /// <summary>Raw text extracted from the document for downstream processing.</summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>Identifier of the classified type (mirrors SourceType for compatibility).</summary>
    public string? ClassifiedTypeId { get; set; }
        = null;

    /// <summary>Version of the classified type definition used during classification.</summary>
    public int? ClassifiedTypeVersion { get; set; }
        = null;

    /// <summary>Confidence score returned by the classifier.</summary>
    public double ClassificationConfidence { get; set; }
        = 0.0;

    /// <summary>Method used to classify the document.</summary>
    public ClassificationMethod ClassificationMethod { get; set; }
        = ClassificationMethod.Heuristic;

    /// <summary>Classifier-provided explanation or reasoning string.</summary>
    public string? ClassificationReason { get; set; }
        = null;

    /// <summary>Detected document style (Narrative, SparseForm, Dialogue, etc.).</summary>
    public string? DocumentStyleCode { get; set; }
        = null;

    /// <summary>Document style entity ID for reference.</summary>
    public string? DocumentStyleId { get; set; }
        = null;

    /// <summary>Version of the document style definition used during classification.</summary>
    public int? DocumentStyleVersion { get; set; }
        = null;

    /// <summary>Confidence score for document style classification.</summary>
    public double DocumentStyleConfidence { get; set; }
        = 0.0;

    /// <summary>Reasoning provided by document style classifier.</summary>
    public string? DocumentStyleReason { get; set; }
        = null;

    /// <summary>When document style classification was performed.</summary>
    public DateTime? DocumentStyleClassifiedAt { get; set; }
        = null;

    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Pending;

    public double ExtractionConfidence { get; set; }
        = 0.0;
    public DateTime? ExtractedAt { get; set; }
        = null;

    /// <summary>
    /// Version of the SourceType (AnalysisType) used during the last extraction.
    /// Used to detect when the schema has changed and extraction should be redone.
    /// </summary>
    public int? LastExtractedAnalysisTypeVersion { get; set; }
        = null;

    public string TextHash { get; set; } = string.Empty;
    public int PageCount { get; set; }
        = 0;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum DocumentProcessingStatus
{
    Pending,
    Extracted,
    Indexed,
    Classified,
    Failed
}

public enum ClassificationMethod
{
    Heuristic,
    Vector,
    Llm,
    Manual
}
