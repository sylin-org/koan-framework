using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
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

    [Column(TypeName = "jsonb")]
    public Dictionary<string, DocumentProcessingStageState> StageTelemetry { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public DocumentProcessingStageState GetStageState(DocumentProcessingStage stage)
    {
        var key = stage.ToString();
        if (!StageTelemetry.TryGetValue(key, out var state))
        {
            state = new DocumentProcessingStageState
            {
                Stage = stage,
                LastStatus = DocumentProcessingStatus.Queued
            };
            StageTelemetry[key] = state;
        }

        return state;
    }

    public void MarkStageQueued(DocumentProcessingStage stage, DateTimeOffset queuedAt, string? correlationId)
    {
        var state = GetStageState(stage);
        state.LastStatus = DocumentProcessingStatus.Queued;
        state.LastQueuedAt = queuedAt;
        state.LastCorrelationId = correlationId;
    }
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

public sealed class DocumentProcessingStageState
{
    public const int MaxAttemptHistory = 10;

    [Required]
    public DocumentProcessingStage Stage { get; set; }
        = DocumentProcessingStage.Upload;

    [Required]
    public DocumentProcessingStatus LastStatus { get; set; }
        = DocumentProcessingStatus.Uploaded;

    public DateTimeOffset? LastQueuedAt { get; set; }
        = null;

    public DateTimeOffset? LastStartedAt { get; set; }
        = null;

    public DateTimeOffset? LastCompletedAt { get; set; }
        = null;

    public TimeSpan? LastDuration { get; set; }
        = null;

    public int AttemptCount { get; set; }
        = 0;

    public int SuccessCount { get; set; }
        = 0;

    public int FailureCount { get; set; }
        = 0;

    public string? LastError { get; set; }
        = null;

    public long? LastInputTokens { get; set; }
        = null;

    public long? LastOutputTokens { get; set; }
        = null;

    public string? LastCorrelationId { get; set; }
        = null;

    public List<DocumentProcessingStageAttempt> Attempts { get; set; }
        = new();

    public void AppendAttempt(DocumentProcessingStageAttempt attempt)
    {
        Attempts.Add(attempt);
        if (Attempts.Count > MaxAttemptHistory)
        {
            var skip = Attempts.Count - MaxAttemptHistory;
            Attempts = Attempts.Skip(skip).ToList();
        }
    }
}

public sealed class DocumentProcessingStageAttempt
{
    public int Attempt { get; set; }
        = 1;

    public DateTimeOffset StartedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }
        = null;

    public DocumentProcessingStatus Status { get; set; }
        = DocumentProcessingStatus.Queued;

    public TimeSpan? Duration { get; set; }
        = null;

    public string? Error { get; set; }
        = null;

    public long? InputTokens { get; set; }
        = null;

    public long? OutputTokens { get; set; }
        = null;
}
