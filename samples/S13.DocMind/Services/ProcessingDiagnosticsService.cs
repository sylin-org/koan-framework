using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentProcessingDiagnostics
{
    Task<ProcessingQueueResult> GetQueueAsync(ProcessingQueueQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken);
    Task<ProcessingRetryResult> RetryAsync(string documentId, ProcessingRetryRequest request, CancellationToken cancellationToken);
    Task<ProcessingConfigResponse> GetConfigAsync(CancellationToken cancellationToken);
    Task<ProcessingReplayResult> ReplayAsync(ProcessingReplayRequest request, CancellationToken cancellationToken);
    Task<DocumentDiscoveryValidationResult> ValidateDiscoveryAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken);
}

public sealed class DocumentProcessingDiagnostics : IDocumentProcessingDiagnostics
{
    private readonly IDocumentProcessingEventSink _eventSink;
    private readonly IDocumentIntakeService _intakeService;
    private readonly IDocumentDiscoveryRefreshScheduler _refreshScheduler;
    private readonly IDocumentDiscoveryRefresher _discoveryRefresher;
    private readonly DocMindVectorHealth _vectorHealth;
    private readonly DocMindOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentProcessingDiagnostics> _logger;

    public DocumentProcessingDiagnostics(
        IDocumentProcessingEventSink eventSink,
        IDocumentIntakeService intakeService,
        IDocumentDiscoveryRefreshScheduler refreshScheduler,
        IDocumentDiscoveryRefresher discoveryRefresher,
        DocMindVectorHealth vectorHealth,
        IOptions<DocMindOptions> options,
        TimeProvider clock,
        ILogger<DocumentProcessingDiagnostics> logger)
    {
        _eventSink = eventSink;
        _intakeService = intakeService;
        _refreshScheduler = refreshScheduler;
        _discoveryRefresher = discoveryRefresher;
        _vectorHealth = vectorHealth;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task<ProcessingConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
    {
        var summary = CreateDiagnosticsSummary();

        var response = new ProcessingConfigResponse
        {
            MaxConcurrency = _options.Processing.MaxConcurrency,
            WorkerBatchSize = _options.Processing.WorkerBatchSize,
            PollIntervalSeconds = _options.Processing.PollIntervalSeconds,
            Vector = summary.Vector,
            Discovery = summary.Discovery
        };

        return Task.FromResult(response);
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

        var slice = await DocumentProcessingJobQueries.QueryAsync(jobQuery, cancellationToken).ConfigureAwait(false);
        var ordered = slice.Items
            .OrderBy(job => job.NextAttemptAt ?? job.CreatedAt)
            .ThenBy(job => job.CreatedAt)
            .ToList();

        var skip = pageSize * (page - 1);
        var pageItems = ordered.Skip(skip).Take(pageSize).ToList();

        var hasMore = ordered.Count > skip + pageItems.Count;

        var summary = CreateDiagnosticsSummary();

        if (pageItems.Count == 0)
        {
            return new ProcessingQueueResult
            {
                Items = Array.Empty<ProcessingQueueItem>(),
                HasMore = hasMore,
                AsOf = _clock.GetUtcNow(),
                Page = page,
                PageSize = pageSize,
                Diagnostics = summary
            };
        }

        var documents = await GetDocumentsAsync(pageItems.Select(job => job.SourceDocumentId), cancellationToken).ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var items = pageItems
            .Select(job => MapQueueItem(job, documents, now))
            .ToList();

        return new ProcessingQueueResult
        {
            Items = items,
            HasMore = hasMore,
            AsOf = now,
            Page = page,
            PageSize = pageSize,
            Diagnostics = summary
        };
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
        var events = await QueryProcessingEventsAsync(
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
            DocumentProcessingStage.GenerateEmbeddings => DocumentProcessingStage.GenerateChunks,
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

        var job = await DocumentProcessingJobQueries.FindByDocumentAsync(parsedId, cancellationToken).ConfigureAwait(false)
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
        return ProcessingRetryResult.Succeed(document.Id, document.Status);
    }

    public async Task<ProcessingReplayResult> ReplayAsync(ProcessingReplayRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DocumentId))
        {
            return ProcessingReplayResult.Invalid("Document id required");
        }

        if (!Guid.TryParse(request.DocumentId, out var documentId))
        {
            return ProcessingReplayResult.Invalid("Document id must be a GUID");
        }

        var document = await SourceDocument.Get(request.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return ProcessingReplayResult.NotFound(request.DocumentId);
        }

        if (request.Reset)
        {
            await PurgeDocumentStateAsync(document, cancellationToken).ConfigureAwait(false);
        }

        var stage = request.Stage;
        await _intakeService.RequeueAsync(request.DocumentId, stage, cancellationToken).ConfigureAwait(false);

        await _eventSink.RecordAsync(
            new DocumentProcessingEventEntry(
                documentId,
                DocumentProcessingStage.Upload,
                DocumentProcessingStatus.Queued,
                Detail: "Replay requested",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["stage"] = stage.ToString(),
                    ["reset"] = request.Reset.ToString()
                },
                CorrelationId: Guid.NewGuid().ToString("N")),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Replay queued for document {DocumentId} at stage {Stage}", request.DocumentId, stage);
        return ProcessingReplayResult.Queued(request.DocumentId, stage);
    }

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static async Task PurgeDocumentStateAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(document.Id, out var documentId))
        {
            return;
        }

        var chunkFilter = $"SourceDocumentId == '{documentId}'";
        var chunks = await DocumentChunk.Query(chunkFilter, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
        {
            await chunk.Delete(cancellationToken).ConfigureAwait(false);
        }

        var embeddings = await DocumentChunkEmbedding.Query(chunkFilter, cancellationToken).ConfigureAwait(false);
        foreach (var embedding in embeddings)
        {
            await embedding.Delete(cancellationToken).ConfigureAwait(false);
        }

        var insights = await DocumentInsight.Query($"SourceDocumentId == '{documentId}'", cancellationToken).ConfigureAwait(false);
        foreach (var insight in insights)
        {
            await insight.Delete(cancellationToken).ConfigureAwait(false);
        }

        document.Status = DocumentProcessingStatus.Queued;
        document.LastProcessedAt = null;
        document.LastError = null;
        document.Summary = new DocumentProcessingSummary();
        await document.Save(cancellationToken).ConfigureAwait(false);
    }

    public Task<DocumentDiscoveryValidationResult> ValidateDiscoveryAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken)
    {
        var effective = request ?? new DocumentDiscoveryValidationRequest();
        return _discoveryRefresher.ValidateAsync(effective, cancellationToken);
    }

    private ProcessingDiagnosticsSummary CreateDiagnosticsSummary()
    {
        var vector = _vectorHealth.Snapshot();
        var discovery = _refreshScheduler.Snapshot();

        return new ProcessingDiagnosticsSummary
        {
            Vector = MapVector(vector),
            Discovery = MapDiscovery(discovery)
        };
    }

    private static VectorReadinessResponse MapVector(DocMindVectorReadinessSnapshot snapshot)
        => new()
        {
            AdapterAvailable = snapshot.AdapterAvailable,
            FallbackActive = snapshot.FallbackActive,
            LastAuditAt = snapshot.LastAuditAt,
            LastAuditError = snapshot.LastAuditError,
            MissingProfiles = snapshot.MissingProfiles,
            LastSearchLatencyMs = snapshot.LastSearchLatencyMs,
            LastGenerationDurationMs = snapshot.LastGenerationDurationMs,
            LastSearchAt = snapshot.LastSearchAt,
            LastGenerationAt = snapshot.LastGenerationAt,
            LastAdapterModel = snapshot.LastAdapterModel
        };

    private static DiscoveryRefreshResponse MapDiscovery(DocumentDiscoveryRefreshStatus status)
        => new()
        {
            Pending = status.PendingCount,
            LastCompletedAt = status.LastCompletedAt,
            LastDurationMs = status.LastDuration?.TotalMilliseconds,
            LastError = status.LastError,
            LastQueuedAt = status.LastQueuedAt,
            LastReason = status.LastReason,
            LastStartedAt = status.LastStartedAt,
            TotalCompleted = status.TotalCompleted,
            TotalFailed = status.TotalFailed,
            AverageDurationMs = status.AverageDuration?.TotalMilliseconds,
            MaxDurationMs = status.MaxDuration?.TotalMilliseconds
        };

    private static async Task<IDictionary<Guid, SourceDocument>> GetDocumentsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var idArray = ids.Distinct().ToArray();
        if (idArray.Length == 0)
        {
            return new Dictionary<Guid, SourceDocument>();
        }

        var filter = string.Join(" || ", idArray.Select(id => $"Id == '{id}'"));
        var results = await SourceDocument.Query(filter, cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<Guid, SourceDocument>(idArray.Length);
        foreach (var entity in results)
        {
            if (Guid.TryParse(entity.Id, out var parsed))
            {
                map[parsed] = entity;
            }
        }

        return map;
    }

    private static async Task<IReadOnlyList<DocumentProcessingEvent>> QueryProcessingEventsAsync(
        Guid? documentId,
        DocumentProcessingStage? stage,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var filters = new List<string>();
        if (documentId.HasValue)
        {
            filters.Add($"SourceDocumentId == '{documentId.Value}'");
        }

        if (stage.HasValue)
        {
            filters.Add($"Stage == {(int)stage.Value}");
        }

        var filterExpression = filters.Count == 0 ? null : string.Join(" && ", filters);
        var events = filterExpression is null
            ? await DocumentProcessingEvent.All(cancellationToken).ConfigureAwait(false)
            : await DocumentProcessingEvent.Query(filterExpression, cancellationToken).ConfigureAwait(false);

        if (from.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt >= from.Value).ToList();
        }

        if (to.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt <= to.Value).ToList();
        }

        return events
            .OrderBy(evt => evt.SourceDocumentId)
            .ThenBy(evt => evt.CreatedAt)
            .ToList();
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

    public static ProcessingRetryResult Succeed(string documentId, DocumentProcessingStatus status)
        => new(true, documentId, status, null);
}

public sealed class ProcessingQueueResult
{
    public IReadOnlyCollection<ProcessingQueueItem> Items { get; set; } = Array.Empty<ProcessingQueueItem>();
    public bool HasMore { get; set; }
        = false;
    public DateTimeOffset AsOf { get; set; }
        = DateTimeOffset.UtcNow;
    public int Page { get; set; }
        = 1;
    public int PageSize { get; set; }
        = 20;
    public ProcessingDiagnosticsSummary Diagnostics { get; set; }
        = new();
}

public sealed class ProcessingConfigResponse
{
    public int MaxConcurrency { get; set; }
    public int WorkerBatchSize { get; set; }
    public int PollIntervalSeconds { get; set; }
    public VectorReadinessResponse Vector { get; set; } = new();
    public DiscoveryRefreshResponse Discovery { get; set; } = new();
}

public sealed class ProcessingDiagnosticsSummary
{
    public VectorReadinessResponse Vector { get; set; } = new();
    public DiscoveryRefreshResponse Discovery { get; set; } = new();
}

public sealed class VectorReadinessResponse
{
    public bool AdapterAvailable { get; set; }
    public bool FallbackActive { get; set; }
    public DateTimeOffset? LastAuditAt { get; set; }
    public string? LastAuditError { get; set; }
    public IReadOnlyList<string> MissingProfiles { get; set; } = Array.Empty<string>();
    public double? LastSearchLatencyMs { get; set; }
    public double? LastGenerationDurationMs { get; set; }
    public DateTimeOffset? LastSearchAt { get; set; }
    public DateTimeOffset? LastGenerationAt { get; set; }
    public string? LastAdapterModel { get; set; }
}

public sealed class DiscoveryRefreshResponse
{
    public int Pending { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public double? LastDurationMs { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastQueuedAt { get; set; }
    public string? LastReason { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public long TotalCompleted { get; set; }
    public long TotalFailed { get; set; }
    public double? AverageDurationMs { get; set; }
    public double? MaxDurationMs { get; set; }
}

public sealed class ProcessingReplayRequest
{
    public string DocumentId { get; set; } = string.Empty;
    public DocumentProcessingStage Stage { get; set; } = DocumentProcessingStage.ExtractText;
    public bool Reset { get; set; }
}

public sealed class ProcessingReplayResult
{
    private ProcessingReplayResult(bool success, string message, string? documentId, DocumentProcessingStage stage)
    {
        Success = success;
        Message = message;
        DocumentId = documentId;
        Stage = stage;
    }

    public bool Success { get; }
    public string Message { get; }
    public string? DocumentId { get; }
    public DocumentProcessingStage Stage { get; }

    public static ProcessingReplayResult Queued(string documentId, DocumentProcessingStage stage)
        => new(true, "Replay queued", documentId, stage);

    public static ProcessingReplayResult NotFound(string documentId)
        => new(false, "Document not found", documentId, DocumentProcessingStage.Upload);

    public static ProcessingReplayResult Invalid(string message)
        => new(false, message, null, DocumentProcessingStage.Upload);
}

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
