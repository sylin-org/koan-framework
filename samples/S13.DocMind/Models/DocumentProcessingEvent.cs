using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Flow.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Event stream entity capturing telemetry for each stage of document processing.
/// Provides detailed instrumentation for diagnostics, retries, and workflow visualization.
/// </summary>
public sealed class DocumentProcessingEvent : FlowEntity<DocumentProcessingEvent>
{
    [Required]
    public Guid DocumentId { get; set; }
        = Guid.Empty;

    public Guid? ChunkId { get; set; }
        = null;

    public Guid? InsightId { get; set; }
        = null;

    [Required]
    public DocumentProcessingStage Stage { get; set; }
        = DocumentProcessingStage.Uploaded;

    [Required]
    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Queued;

    public DateTimeOffset OccurredAt { get; set; }
        = DateTimeOffset.UtcNow;

    public TimeSpan? Duration { get; set; }
        = null;

    public int Attempt { get; set; }
        = 1;

    [MaxLength(100)]
    public string? CorrelationId { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public object? EventData { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> Metrics { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Tags { get; set; }
        = new();

    public long? InputTokens { get; set; }
        = null;

    public long? OutputTokens { get; set; }
        = null;

    public double? ConfidenceScore { get; set; }
        = null;

    public int? VisionFrameCount { get; set; }
        = null;

    public double? VisionConfidence { get; set; }
        = null;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
        = null;

    [MaxLength(200)]
    public string? ErrorCode { get; set; }
        = null;

    public bool IsTerminal { get; set; }
        = false;
}
