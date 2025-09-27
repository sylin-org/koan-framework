using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentProcessingDiagnostics
{
    Task<IReadOnlyCollection<ProcessingQueueItem>> GetQueueAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken);
    Task<ProcessingRetryResult> RetryAsync(string documentId, ProcessingRetryRequest request, CancellationToken cancellationToken);
}

public sealed class DocumentProcessingDiagnostics : IDocumentProcessingDiagnostics
{
    private readonly IDocumentPipelineQueue _queue;
    private readonly IDocumentProcessingEventSink _eventSink;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentProcessingDiagnostics> _logger;

    public DocumentProcessingDiagnostics(
        IDocumentPipelineQueue queue,
        IDocumentProcessingEventSink eventSink,
        TimeProvider clock,
        ILogger<DocumentProcessingDiagnostics> logger)
    {
        _queue = queue;
        _eventSink = eventSink;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<ProcessingQueueItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        var snapshot = _queue.GetSnapshot();
        if (snapshot.Count == 0)
        {
            return Array.Empty<ProcessingQueueItem>();
        }

        var fetchTasks = snapshot
            .Select(item => SourceDocument.Get(item.DocumentId.ToString(), cancellationToken))
            .ToArray();

        await Task.WhenAll(fetchTasks).ConfigureAwait(false);

        var documents = new Dictionary<Guid, SourceDocument>();
        for (var i = 0; i < fetchTasks.Length; i++)
        {
            var entity = fetchTasks[i].Result;
            if (entity is null)
            {
                continue;
            }

            if (Guid.TryParse(entity.Id, out var parsed))
            {
                documents[parsed] = entity;
            }
        }

        var now = _clock.GetUtcNow();

        return snapshot
            .OrderBy(item => item.EnqueuedAt)
            .Select(item =>
            {
                documents.TryGetValue(item.DocumentId, out var document);
                return new ProcessingQueueItem
                {
                    WorkId = item.WorkId,
                    DocumentId = document?.Id ?? item.DocumentId.ToString(),
                    FileName = document?.DisplayName ?? document?.FileName ?? item.DocumentId.ToString(),
                    Stage = item.Stage,
                    Status = item.Status,
                    Attempt = item.Attempt,
                    RetryCount = item.RetryCount,
                    MaxAttempts = item.MaxAttempts,
                    CorrelationId = item.CorrelationId,
                    EnqueuedAt = item.EnqueuedAt,
                    LastDequeuedAt = item.LastDequeuedAt,
                    LastAttemptCompletedAt = item.LastAttemptCompletedAt,
                    QueueAge = now - item.EnqueuedAt,
                    UploadedAt = document?.UploadedAt ?? item.EnqueuedAt,
                    LastProcessedAt = document?.LastProcessedAt,
                    AssignedProfileId = document?.AssignedProfileId,
                    LastError = document?.LastError
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var events = await DocumentProcessingEvent.All(cancellationToken).ConfigureAwait(false);

        var filteredEvents = events.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.DocumentId) && Guid.TryParse(query.DocumentId, out var docId))
        {
            filteredEvents = filteredEvents.Where(evt => evt.SourceDocumentId == docId);
        }

        if (query.Stage.HasValue)
        {
            filteredEvents = filteredEvents.Where(evt => evt.Stage == query.Stage.Value);
        }

        if (query.From.HasValue)
        {
            filteredEvents = filteredEvents.Where(evt => evt.CreatedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            filteredEvents = filteredEvents.Where(evt => evt.CreatedAt <= query.To.Value);
        }

        var grouped = filteredEvents
            .GroupBy(evt => evt.SourceDocumentId)
            .Select(group =>
            {
                var document = documents.FirstOrDefault(d => Guid.TryParse(d.Id, out var id) && id == group.Key);
                return new ProcessingTimelineEntry
                {
                    DocumentId = document?.Id ?? group.Key.ToString(),
                    FileName = document?.DisplayName ?? document?.FileName ?? group.Key.ToString(),
                    Events = group
                        .OrderBy(evt => evt.CreatedAt)
                        .Select(evt => new ProcessingTimelineEvent
                        {
                            Stage = evt.Stage,
                            Status = evt.Status,
                            Detail = evt.Detail ?? string.Empty,
                            Error = evt.Error,
                            CreatedAt = evt.CreatedAt,
                            Attempt = evt.Attempt,
                            CorrelationId = evt.CorrelationId,
                            Duration = evt.Duration,
                            ChunkId = evt.ChunkId,
                            InsightId = evt.InsightId,
                            Metrics = new Dictionary<string, double>(evt.Metrics),
                            Context = new Dictionary<string, string>(evt.Context, StringComparer.OrdinalIgnoreCase),
                            InputTokens = evt.InputTokens,
                            OutputTokens = evt.OutputTokens,
                            IsTerminal = evt.IsTerminal
                        })
                        .ToList()
                };
            })
            .OrderBy(entry => entry.Events.FirstOrDefault()?.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        return grouped;
    }

    public async Task<ProcessingRetryResult> RetryAsync(string documentId, ProcessingRetryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return ProcessingRetryResult.NotFound(documentId);
        }

        if (!Guid.TryParse(documentId, out var parsedId))
        {
            return ProcessingRetryResult.NotFound(documentId);
        }

        var document = await SourceDocument.Get(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return ProcessingRetryResult.NotFound(documentId);
        }

        document.Status = DocumentProcessingStatus.Queued;
        document.LastError = null;
        document.LastProcessedAt = _clock.GetUtcNow();
        await document.Save(cancellationToken).ConfigureAwait(false);

        await _eventSink.RecordAsync(
            new DocumentProcessingEventEntry(
                parsedId,
                DocumentProcessingStage.Upload,
                DocumentProcessingStatus.Queued,
                Detail: "Retry requested",
                CorrelationId: Guid.NewGuid().ToString("N")),
            cancellationToken).ConfigureAwait(false);

        var work = new DocumentWorkItem(parsedId, DocumentProcessingStage.ExtractText, DocumentProcessingStatus.Queued);
        await _queue.EnqueueAsync(work, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Retry queued for document {DocumentId}", documentId);
        return ProcessingRetryResult.Success(document.Id, document.Status);
    }
}

public sealed class ProcessingTimelineQuery
{
    public string? DocumentId { get; set; }
    public DocumentProcessingStage? Stage { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public sealed class ProcessingTimelineEntry
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public IReadOnlyCollection<ProcessingTimelineEvent> Events { get; set; } = Array.Empty<ProcessingTimelineEvent>();
}

public sealed class ProcessingTimelineEvent
{
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Attempt { get; set; }
    public string? CorrelationId { get; set; }
    public TimeSpan? Duration { get; set; }
    public Guid? ChunkId { get; set; }
    public Guid? InsightId { get; set; }
    public Dictionary<string, double> Metrics { get; set; } = new();
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public bool IsTerminal { get; set; }
}

public sealed class ProcessingQueueItem
{
    public Guid WorkId { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public int Attempt { get; set; }
    public int RetryCount { get; set; }
    public int MaxAttempts { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? LastDequeuedAt { get; set; }
    public DateTimeOffset? LastAttemptCompletedAt { get; set; }
    public TimeSpan QueueAge { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? LastProcessedAt { get; set; }
    public string? AssignedProfileId { get; set; }
    public string? LastError { get; set; }
}

public sealed class ProcessingRetryRequest
{
    public bool Force { get; set; }
}

public sealed class ProcessingRetryResult
{
    private ProcessingRetryResult(bool success, string documentId, DocumentProcessingStatus? status, string? message)
    {
        Success = success;
        DocumentId = documentId;
        Status = status;
        Message = message;
    }

    public bool Success { get; }
    public string DocumentId { get; }
    public DocumentProcessingStatus? Status { get; }
    public string? Message { get; }

    public static ProcessingRetryResult NotFound(string documentId)
        => new(false, documentId, null, "Document not found");

    public static ProcessingRetryResult Success(string documentId, DocumentProcessingStatus status)
        => new(true, documentId, status, null);
}
