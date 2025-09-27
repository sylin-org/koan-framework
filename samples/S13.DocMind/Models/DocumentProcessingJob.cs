using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Durable ledger describing the outstanding work required to process a source document.
/// Persisted jobs let the background worker resume at the precise stage where execution
/// previously stopped without relying on volatile in-memory queues.
/// </summary>
[McpEntity(Name = "document-processing-jobs", Description = "Stage-aware DocMind processing ledger.")]
public sealed class DocumentProcessingJob : Entity<DocumentProcessingJob>
{
    [Parent(typeof(SourceDocument))]
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    [Required]
    public DocumentProcessingStage Stage { get; set; }
        = DocumentProcessingStage.ExtractText;

    [Required]
    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Queued;

    [MaxLength(64)]
    public string CorrelationId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(0, int.MaxValue)]
    public int Attempt { get; set; }
        = 0;

    [Range(0, int.MaxValue)]
    public int RetryCount { get; set; }
        = 0;

    [Range(1, 50)]
    public int MaxAttempts { get; set; }
        = 5;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }
        = null;

    public DateTimeOffset? CompletedAt { get; set; }
        = null;

    public DateTimeOffset? NextAttemptAt { get; set; }
        = null;

    [MaxLength(1024)]
    public string? LastError { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public DocumentExtractionSnapshot? Extraction { get; set; }
        = null;
}

/// <summary>
/// Minimal representation of the text extraction output used to resume downstream stages
/// without rerunning the extractor after partial failures.
/// </summary>
public sealed class DocumentExtractionSnapshot
{
    [Required]
    public string Text { get; set; } = string.Empty;

    public int WordCount { get; set; }
        = 0;

    public int PageCount { get; set; }
        = 0;

    public bool ContainsImages { get; set; }
        = false;

    [MaxLength(32)]
    public string? Language { get; set; }
        = null;
}
