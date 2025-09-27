using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Represents an uploaded source document with immutable storage metadata and the latest processing summary.
/// Mirrors the refactoring blueprint entity so downstream services and controllers share a stable contract.
/// </summary>
[McpEntity(Name = "source-documents", Description = "Uploaded documents pending or completing DocMind analysis.")]
public sealed class SourceDocument : Entity<SourceDocument>
{
    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? DisplayName { get; set; }
        = null;

    [Required, MaxLength(120)]
    public string ContentType { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long FileSizeBytes { get; set; }
        = 0L;

    [Required, StringLength(128, MinimumLength = 128)]
    public string Sha512 { get; set; } = string.Empty;

    [MaxLength(120)]
    public string StorageBucket { get; set; } = "local";

    [MaxLength(512)]
    public string StorageObjectKey { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? StorageVersionId { get; set; }
        = null;

    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Uploaded;

    public DateTimeOffset UploadedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastProcessedAt { get; set; }
        = null;

    [MaxLength(1024)]
    public string? LastError { get; set; }
        = null;

    [MaxLength(200)]
    public string? AssignedProfileId { get; set; }
        = null;

    public bool AssignedBySystem { get; set; }
        = false;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Tags { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    [MaxLength(2000)]
    public string? Description { get; set; }
        = null;

    public DocumentProcessingSummary Summary { get; set; }
        = new();
}

/// <summary>
/// Aggregated processing summary with chunk and insight references for quick projections.
/// </summary>
public sealed class DocumentProcessingSummary
{
    public bool TextExtracted { get; set; }
        = false;

    public bool VisionExtracted { get; set; }
        = false;

    public double? AutoClassificationConfidence { get; set; }
        = null;

    [MaxLength(2000)]
    public string? PrimaryFindings { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<InsightReference> InsightRefs { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<ChunkReference> ChunkRefs { get; set; }
        = new();
}

/// <summary>
/// Lightweight pointer to an insight for dashboards and summary widgets.
/// </summary>
public sealed class InsightReference
{
    public Guid InsightId { get; set; }
        = Guid.Empty;

    public InsightChannel Channel { get; set; }
        = InsightChannel.Text;

    public double? Confidence { get; set; }
        = null;

    [MaxLength(200)]
    public string? Heading { get; set; }
        = null;
}

/// <summary>
/// Lightweight pointer to a chunk for quick navigation without additional queries.
/// </summary>
public sealed class ChunkReference
{
    public Guid ChunkId { get; set; }
        = Guid.Empty;

    public int Order { get; set; }
        = 0;

    [MaxLength(120)]
    public string? Section { get; set; }
        = null;
}

/// <summary>
/// Pipeline stages exposed through diagnostics and timeline projections.
/// </summary>
public enum DocumentProcessingStage
{
    Upload = 0,
    Deduplicate,
    ExtractText,
    ExtractVision,
    GenerateChunks,
    GenerateInsights,
    GenerateEmbeddings,
    Aggregate,
    Complete,
    Failed
}

/// <summary>
/// Document lifecycle status reflected on the SourceDocument record.
/// </summary>
public enum DocumentProcessingStatus
{
    Uploaded = 0,
    Queued,
    Extracting,
    Extracted,
    Analyzing,
    InsightsReady,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Channels used when classifying insights and diagnostic events.
/// </summary>
public enum InsightChannel
{
    Text = 0,
    Vision,
    Aggregation,
    UserFeedback
}
