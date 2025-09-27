using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Event stream entity capturing telemetry for each stage of document processing.
/// Provides durable diagnostics powering the processing timeline and retry workflows.
/// </summary>
[Parent(typeof(SourceDocument))]
public sealed class DocumentProcessingEvent : Entity<DocumentProcessingEvent>
{
    [Required]
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    public Guid? ChunkId { get; set; }
        = null;

    public Guid? InsightId { get; set; }
        = null;

    [Required]
    public DocumentProcessingStage Stage { get; set; }
        = DocumentProcessingStage.Upload;

    [Required]
    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Uploaded;

    [MaxLength(300)]
    public string? Detail { get; set; }
        = null;

    [MaxLength(2000)]
    public string? Error { get; set; }
        = null;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public TimeSpan? Duration { get; set; }
        = null;

    public int Attempt { get; set; }
        = 1;

    [MaxLength(100)]
    public string? CorrelationId { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Context { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> Metrics { get; set; }
        = new();

    public long? InputTokens { get; set; }
        = null;

    public long? OutputTokens { get; set; }
        = null;

    public bool IsTerminal { get; set; }
        = false;
}
