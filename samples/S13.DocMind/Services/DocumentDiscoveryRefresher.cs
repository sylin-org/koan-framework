using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentDiscoveryRefresher
{
    Task<DocumentDiscoveryProjection> RefreshAsync(CancellationToken cancellationToken);
    Task<DocumentDiscoveryValidationResult> ValidateAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken);
}

public sealed class DocumentDiscoveryRefresher : IDocumentDiscoveryRefresher
{
    private readonly TimeProvider _clock;

    public DocumentDiscoveryRefresher(TimeProvider clock)
    {
        _clock = clock;
    }

    public Task<DocumentDiscoveryProjection> RefreshAsync(CancellationToken cancellationToken)
        => DocumentDiscoveryProjectionBuilder.RefreshAsync(_clock, cancellationToken);

    public async Task<DocumentDiscoveryValidationResult> ValidateAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken)
    {
        var effective = request ?? new DocumentDiscoveryValidationRequest();
        var started = _clock.GetUtcNow();

        var projection = await DocumentDiscoveryProjection.Get("global", cancellationToken).ConfigureAwait(false);
        var refreshed = false;

        var thresholdMinutes = effective.RefreshIfOlderThanMinutes ?? 10;
        thresholdMinutes = Math.Clamp(thresholdMinutes, 1, 120);
        var threshold = TimeSpan.FromMinutes(thresholdMinutes);

        if (projection is null || effective.ForceRefresh || (started - projection.RefreshedAt) > threshold)
        {
            projection = await DocumentDiscoveryProjectionBuilder.RefreshAsync(_clock, cancellationToken).ConfigureAwait(false);
            refreshed = true;
        }

        projection ??= new DocumentDiscoveryProjection
        {
            RefreshedAt = started,
            Queue = new DocumentQueueProjection { AsOf = started }
        };

        var documents = (await SourceDocument.All(cancellationToken).ConfigureAwait(false)).ToList();
        var insights = (await DocumentInsight.All(cancellationToken).ConfigureAwait(false)).ToList();

        var warnings = new List<string>();
        var completedDocuments = documents.Count(d => d.Status == DocumentProcessingStatus.Completed);
        var activeDocuments = documents.Count(d => d.Status is DocumentProcessingStatus.Queued or DocumentProcessingStatus.Extracting or DocumentProcessingStatus.Analyzing);
        var failedDocuments = documents.Count(d => d.Status == DocumentProcessingStatus.Failed);

        if (projection.Overview.TotalDocuments != documents.Count)
        {
            warnings.Add($"projection totalDocuments mismatch (projection={projection.Overview.TotalDocuments}, actual={documents.Count})");
        }

        if (projection.Overview.CompletedDocuments != completedDocuments)
        {
            warnings.Add($"projection completedDocuments mismatch (projection={projection.Overview.CompletedDocuments}, actual={completedDocuments})");
        }

        if (projection.Overview.ActiveDocuments != activeDocuments)
        {
            warnings.Add($"projection activeDocuments mismatch (projection={projection.Overview.ActiveDocuments}, actual={activeDocuments})");
        }

        if (projection.Overview.FailedDocuments != failedDocuments)
        {
            warnings.Add($"projection failedDocuments mismatch (projection={projection.Overview.FailedDocuments}, actual={failedDocuments})");
        }

        var completed = _clock.GetUtcNow();
        var duration = completed - started;
        var scheduler = DocumentDiscoveryRefreshService.LatestStatus;

        var result = new DocumentDiscoveryValidationResult
        {
            ValidatedAt = completed,
            ProjectionRefreshed = refreshed,
            DurationMs = duration.TotalMilliseconds,
            ProjectionDurationSeconds = projection.RefreshDurationSeconds,
            DocumentCount = documents.Count,
            InsightCount = insights.Count,
            QueueCount = projection.Queue.Pending,
            Warnings = warnings,
            Snapshot = new DiscoveryRefreshResponse
            {
                Pending = scheduler.PendingCount,
                LastCompletedAt = scheduler.LastCompletedAt,
                LastDurationMs = scheduler.LastDuration?.TotalMilliseconds,
                LastError = scheduler.LastError,
                LastQueuedAt = scheduler.LastQueuedAt,
                LastReason = scheduler.LastReason,
                LastStartedAt = scheduler.LastStartedAt,
                TotalCompleted = scheduler.TotalCompleted,
                TotalFailed = scheduler.TotalFailed,
                AverageDurationMs = scheduler.AverageDuration?.TotalMilliseconds,
                MaxDurationMs = scheduler.MaxDuration?.TotalMilliseconds
            }
        };

        if (effective.IncludeOverview)
        {
            result.Overview = projection.Overview;
        }

        if (effective.IncludeCollections)
        {
            result.Collections = projection.Collections;
        }

        if (effective.IncludeQueueEntries)
        {
            result.Queue = projection.Queue;
        }

        return result;
    }
}

public sealed class DocumentDiscoveryValidationRequest
{
    public bool ForceRefresh { get; set; }
    public int? RefreshIfOlderThanMinutes { get; set; }
    public bool IncludeOverview { get; set; }
    public bool IncludeCollections { get; set; }
    public bool IncludeQueueEntries { get; set; }
}

public sealed class DocumentDiscoveryValidationResult
{
    public DateTimeOffset ValidatedAt { get; set; }
    public bool ProjectionRefreshed { get; set; }
    public double DurationMs { get; set; }
    public double? ProjectionDurationSeconds { get; set; }
    public int DocumentCount { get; set; }
    public int InsightCount { get; set; }
    public int QueueCount { get; set; }
    public IReadOnlyCollection<string> Warnings { get; set; } = Array.Empty<string>();
    public DiscoveryRefreshResponse Snapshot { get; set; } = new();
    public DocumentInsightsOverview? Overview { get; set; }
        = null;
    public IReadOnlyCollection<DocumentCollectionSummary> Collections { get; set; }
        = Array.Empty<DocumentCollectionSummary>();
    public DocumentQueueProjection? Queue { get; set; }
        = null;
}
