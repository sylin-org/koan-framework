using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Infrastructure.Repositories;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentProcessingDiagnostics
{
    Task<ProcessingQueueResult> GetQueueAsync(ProcessingQueueQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken);
    Task<ProcessingRetryResult> RetryAsync(string documentId, ProcessingRetryRequest request, CancellationToken cancellationToken);
}

public sealed class DocumentProcessingDiagnostics : IDocumentProcessingDiagnostics
{
    private readonly IDocumentProcessingEventSink _eventSink;
    private readonly DocMindOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentProcessingDiagnostics> _logger;

    public DocumentProcessingDiagnostics(
        IDocumentProcessingEventSink eventSink,
        IOptions<DocMindOptions> options,
        TimeProvider clock,
        ILogger<DocumentProcessingDiagnostics> logger)
    {
        _eventSink = eventSink;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ProcessingQueueResult> GetQueueAsync(ProcessingQueueQuery query, CancellationToken cancellationToken)
    {
        var effectiveQuery = query ?? new ProcessingQueueQuery();
        var page = Math.Max(1, effectiveQuery.Page);
        var pageSize = Math.Clamp(effectiveQuery.PageSize, 1, 100);
        var statuses = ResolveStatuses(effectiveQuery);

        var jobQuery = new DocumentProcessingJobQuery
        {
            Statuses = statuses,
            Stages = effectiveQuery.Stages,
            CorrelationId = string.IsNullOrWhiteSpace(effectiveQuery.CorrelationId) ? null : effectiveQuery.CorrelationId,
            SourceDocumentId = TryParseGuid(effectiveQuery.DocumentId),
            DueBefore = effectiveQuery.IncludeFuture ? null : _clock.GetUtcNow(),
            Take = pageSize * page + 1,
            OrderByDue = true,
            IncludeExtraForPaging = false
        };

        var slice = await DocumentProcessingJobRepository.QueryAsync(jobQuery, cancellationToken).ConfigureAwait(false);
        var ordered = slice.Items
            .OrderBy(job => job.NextAttemptAt ?? job.CreatedAt)
            .ThenBy(job => job.CreatedAt)
            .ToList();

        var skip = pageSize * (page - 1);
        var pageItems = ordered.Skip(skip).Take(pageSize).ToList();

        var hasMore = ordered.Count > skip + pageItems.Count;

        if (pageItems.Count == 0)
        {
            return new ProcessingQueueResult(Array.Empty<ProcessingQueueItem>(), hasMore, _clock.GetUtcNow(), page, pageSize);
        }

        var documents = await SourceDocumentRepository
            .GetManyAsync(pageItems.Select(job => job.SourceDocumentId), cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var items = pageItems
            .Select(job => MapQueueItem(job, documents, now))
            .ToList();

        return new ProcessingQueueResult(items, hasMore, now, page, pageSize);
    }

    private ProcessingQueueItem MapQueueItem(DocumentProcessingJob job, IDictionary<Guid, SourceDocument> documents, DateTimeOffset now)
    {
        documents.TryGetValue(job.SourceDocumentId, out var document);

        var stages = job.StageTelemetry?.Values
            .Select(MapStageSnapshot)
            .OrderBy(snapshot => snapshot.Stage)
            .ToList() ?? new List<ProcessingStageSnapshot>();

        return new ProcessingQueueItem
        {
            WorkId = Guid.TryParse(job.Id, out var parsedId) ? parsedId : job.SourceDocumentId,
            DocumentId = document?.Id ?? job.SourceDocumentId.ToString(),
            FileName = document?.DisplayName ?? document?.FileName ?? job.SourceDocumentId.ToString(),
            Stage = job.Stage,
            Status = job.Status,
            Attempt = job.Attempt,
            RetryCount = job.RetryCount,
            MaxAttempts = job.MaxAttempts,
            CorrelationId = job.CorrelationId,
            EnqueuedAt = job.CreatedAt,
            LastDequeuedAt = job.StartedAt,
            LastAttemptCompletedAt = job.CompletedAt,
            QueueAge = now - job.CreatedAt,
            UploadedAt = document?.UploadedAt ?? job.CreatedAt,
            LastProcessedAt = document?.LastProcessedAt,
            AssignedProfileId = document?.AssignedProfileId,
            LastError = job.LastError ?? document?.LastError,
            NextAttemptAt = job.NextAttemptAt,
            StageTelemetry = stages
        };
    }

    private static ProcessingStageSnapshot MapStageSnapshot(DocumentProcessingStageState state)
    {
        var attempts = state.Attempts
            .Select(attempt => new ProcessingStageAttemptSnapshot
            {
                Attempt = attempt.Attempt,
                StartedAt = attempt.StartedAt,
                CompletedAt = attempt.CompletedAt,
                Status = attempt.Status,
                Duration = attempt.Duration,
                Error = attempt.Error,
                InputTokens = attempt.InputTokens,
                OutputTokens = attempt.OutputTokens
            })
            .ToList();

        return new ProcessingStageSnapshot
        {
            Stage = state.Stage,
            LastStatus = state.LastStatus,
            LastQueuedAt = state.LastQueuedAt,
            LastStartedAt = state.LastStartedAt,
            LastCompletedAt = state.LastCompletedAt,
            LastDuration = state.LastDuration,
            LastError = state.LastError,
            LastInputTokens = state.LastInputTokens,
            LastOutputTokens = state.LastOutputTokens,
            LastCorrelationId = state.LastCorrelationId,
            AttemptCount = state.AttemptCount,
            SuccessCount = state.SuccessCount,
            FailureCount = state.FailureCount,
            Attempts = attempts
        };
    }

    private static DocumentProcessingStatus[] ResolveStatuses(ProcessingQueueQuery query)
    {
        var baseSet = query.Statuses is { Length: > 0 }
            ? query.Statuses.ToArray()
            : new[]
            {
                DocumentProcessingStatus.Queued,
                DocumentProcessingStatus.Extracting,
                DocumentProcessingStatus.Analyzing,
                DocumentProcessingStatus.InsightsReady,
                DocumentProcessingStatus.Failed
            };

        var filtered = query.IncludeCompleted
            ? baseSet.Concat(new[] { DocumentProcessingStatus.Completed, DocumentProcessingStatus.Cancelled })
            : baseSet.Where(status => status != DocumentProcessingStatus.Completed && status != DocumentProcessingStatus.Cancelled);

        return filtered.Distinct().ToArray();
    }

    public async Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var documentMap = documents
            .Select(doc => new { Document = doc, Parsed = Guid.TryParse(doc.Id, out var id) ? id : (Guid?)null })
            .Where(tuple => tuple.Parsed.HasValue)
            .ToDictionary(tuple => tuple.Parsed!.Value, tuple => tuple.Document);
        var events = await ProcessingEventRepository.GetTimelineAsync(
            TryParseGuid(query.DocumentId),
            query.Stage,
            query.From,
            query.To,
            cancellationToken).ConfigureAwait(false);

        var grouped = events
            .GroupBy(evt => evt.SourceDocumentId)
            .Select(group =>
            {
                documentMap.TryGetValue(group.Key, out var document);
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

        var now = _clock.GetUtcNow();
        document.Status = DocumentProcessingStatus.Queued;
        document.LastError = null;
        document.LastProcessedAt = now;
        document.Summary.LastKnownStatus = DocumentProcessingStatus.Queued;
        document.Summary.LastCompletedStage = request.Stage switch
        {
            DocumentProcessingStage.Aggregate => DocumentProcessingStage.GenerateInsights,
            DocumentProcessingStage.GenerateInsights => DocumentProcessingStage.GenerateChunks,
            DocumentProcessingStage.ExtractVision => DocumentProcessingStage.GenerateChunks,
            DocumentProcessingStage.ExtractText => DocumentProcessingStage.Upload,
            _ => DocumentProcessingStage.Upload
        };
        document.Summary.LastStageCompletedAt = now;
        await document.Save(cancellationToken).ConfigureAwait(false);

        await _eventSink.RecordAsync(
            new DocumentProcessingEventEntry(
                parsedId,
                DocumentProcessingStage.Upload,
                DocumentProcessingStatus.Queued,
                Detail: "Retry requested",
                Context: request.Stage is null
                    ? null
                    : new Dictionary<string, string>
                    {
                        ["stage"] = request.Stage.Value.ToString()
                    },
                CorrelationId: Guid.NewGuid().ToString("N")),
            cancellationToken).ConfigureAwait(false);

        var job = await DocumentProcessingJobRepository.FindByDocumentAsync(parsedId, cancellationToken).ConfigureAwait(false)
                  ?? new DocumentProcessingJob
                  {
                      SourceDocumentId = parsedId,
                      CreatedAt = _clock.GetUtcNow(),
                      UpdatedAt = _clock.GetUtcNow(),
                      MaxAttempts = _options.Processing.MaxRetryAttempts,
                      NextAttemptAt = _clock.GetUtcNow()
                  };

        job.Stage = request.Stage ?? job.Stage;
        job.Status = DocumentProcessingStatus.Queued;
        job.Attempt = 0;
        job.RetryCount = 0;
        job.NextAttemptAt = _clock.GetUtcNow();
        job.UpdatedAt = _clock.GetUtcNow();
        job.LastError = null;
        if (job.MaxAttempts <= 0)
        {
            job.MaxAttempts = _options.Processing.MaxRetryAttempts;
        }
        if (string.IsNullOrWhiteSpace(job.CorrelationId))
        {
            job.CorrelationId = Guid.NewGuid().ToString("N");
        }

        job.MarkStageQueued(job.Stage, now, job.CorrelationId);

        await job.Save(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Retry queued for document {DocumentId}", documentId);
        return ProcessingRetryResult.Success(document.Id, document.Status);
    }

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
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
    public DateTimeOffset? NextAttemptAt { get; set; }
    public IReadOnlyCollection<ProcessingStageSnapshot> StageTelemetry { get; set; } = Array.Empty<ProcessingStageSnapshot>();
}

public sealed class ProcessingRetryRequest
{
    public bool Force { get; set; }
    public DocumentProcessingStage? Stage { get; set; }
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

public sealed record ProcessingQueueResult(
    IReadOnlyCollection<ProcessingQueueItem> Items,
    bool HasMore,
    DateTimeOffset AsOf,
    int Page,
    int PageSize);

public sealed class ProcessingQueueQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public DocumentProcessingStatus[]? Statuses { get; set; }
    public DocumentProcessingStage[]? Stages { get; set; }
    public string? CorrelationId { get; set; }
    public string? DocumentId { get; set; }
    public bool IncludeCompleted { get; set; }
    public bool IncludeFuture { get; set; }
}

public sealed class ProcessingStageSnapshot
{
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus LastStatus { get; set; }
    public DateTimeOffset? LastQueuedAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public string? LastError { get; set; }
    public long? LastInputTokens { get; set; }
    public long? LastOutputTokens { get; set; }
    public string? LastCorrelationId { get; set; }
    public int AttemptCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public IReadOnlyCollection<ProcessingStageAttemptSnapshot> Attempts { get; set; } = Array.Empty<ProcessingStageAttemptSnapshot>();
}

public sealed class ProcessingStageAttemptSnapshot
{
    public int Attempt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Error { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
}
