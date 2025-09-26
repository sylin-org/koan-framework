using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.AI.Contracts.Models;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Represents an uploaded source document with rich processing metadata and storage references.
/// Mirrors the blueprint entity with explicit adapter/table mappings for relational persistence.
/// </summary>
[McpEntity(Name = "source-documents", Description = "Uploaded documents pending or completing DocMind analysis.")]
public sealed class SourceDocument : Entity<SourceDocument>
{
    private const int DefaultVectorDimensions = 1536;

    // Core document metadata
    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? DisplayName { get; set; }
        = null;

    [Required, MaxLength(150)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }
        = 0L;

    [MaxLength(50)]
    public string? Extension { get; set; }
        = null;

    // Storage pointers
    [MaxLength(255)]
    public string? StorageBucket { get; set; }
        = null;

    [MaxLength(1024)]
    public string? StorageObjectKey { get; set; }
        = null;

    [MaxLength(100)]
    public string? StorageVersionId { get; set; }
        = null;

    // Hashing / deduplication
    [Required, StringLength(128, MinimumLength = 128)]
    public string Sha512Hash { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ContentSignature { get; set; }
        = null;

    // User authored context
    [MaxLength(5000)]
    public string Notes { get; set; } = string.Empty;

    // Processing state
    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Queued;

    public DateTimeOffset UploadedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastProcessedAt { get; set; }
        = null;

    [MaxLength(2000)]
    public string? LastProcessingError { get; set; }
        = null;

    public int ProcessingAttempt { get; set; }
        = 0;

    // Classification metadata
    [MaxLength(100)]
    public string? AssignedTypeCode { get; set; }
        = null;

    public double? AutoClassificationConfidence { get; set; }
        = null;

    // Vector annotations when vector adapters configured
    [Vector(Dimensions = DefaultVectorDimensions, IndexType = "HNSW")]
    public double[]? Embedding { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> EmbeddingAnnotations { get; set; }
        = new();

    // Processing summary (replaces legacy DocumentSummary)
    public DocumentProcessingSummary ProcessingSummary { get; set; }
        = new();
}

/// <summary>
/// Aggregated processing summary with detailed telemetry for the document lifecycle.
/// </summary>
public sealed class DocumentProcessingSummary
{
    // Stage tracking
    public DocumentProcessingStage CurrentStage { get; set; }
        = DocumentProcessingStage.Uploaded;

    public DocumentProcessingStatus CurrentStatus { get; set; }
        = DocumentProcessingStatus.Queued;

    public DateTimeOffset? StageStartedAt { get; set; }
        = null;

    public DateTimeOffset? StageCompletedAt { get; set; }
        = null;

    public Dictionary<DocumentProcessingStage, DateTimeOffset> StageHistory { get; set; }
        = new();

    // Vision processing flags
    public DocumentVisionProcessingSummary Vision { get; set; }
        = new();

    // Auto classification telemetry
    public bool AutoClassificationApplied { get; set; }
        = false;

    public double? AutoClassificationConfidence { get; set; }
        = null;

    public string? AutoClassificationProfile { get; set; }
        = null;

    // Chunking / insights relationships
    public List<Guid> ChunkIds { get; set; }
        = new();

    public List<Guid> InsightIds { get; set; }
        = new();

    // Structured processing metrics
    public Dictionary<string, double> MetricCounters { get; set; }
        = new();

    public Dictionary<string, string> Metadata { get; set; }
        = new();

    [MaxLength(2000)]
    public string? LastError { get; set; }
        = null;
}

/// <summary>
/// Vision-centric processing insights captured during extraction.
/// </summary>
public sealed class DocumentVisionProcessingSummary
{
    public bool VisionRequested { get; set; }
        = false;

    public bool VisionCompleted { get; set; }
        = false;

    public bool ContainsTables { get; set; }
        = false;

    public bool ContainsCharts { get; set; }
        = false;

    public bool ContainsHandwriting { get; set; }
        = false;

    public bool ContainsSignatures { get; set; }
        = false;

    public int? ExtractedFrameCount { get; set; }
        = null;

    public double? AverageVisionConfidence { get; set; }
        = null;
}

/// <summary>
/// Stage definitions for document processing pipeline.
/// </summary>
public enum DocumentProcessingStage
{
    Uploaded = 0,
    Queued,
    ExtractText,
    ExtractVision,
    Chunk,
    Classify,
    Enrich,
    Analyze,
    Aggregate,
    Insights,
    Complete,
    Failed
}

/// <summary>
/// Status definitions for document processing telemetry.
/// </summary>
public enum DocumentProcessingStatus
{
    Queued = 0,
    Running,
    Waiting,
    InsightsReady,
    Completed,
    Failed,
    Cancelled
}
