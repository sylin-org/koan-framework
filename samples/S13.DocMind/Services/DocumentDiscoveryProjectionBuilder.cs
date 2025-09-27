using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public static class DocumentDiscoveryProjectionBuilder
{
    private const string ProjectionId = "global";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly TimeSpan MinimumRefreshWriteInterval = TimeSpan.FromMinutes(1);

    public static async Task<DocumentDiscoveryProjection> RefreshAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var existing = await DocumentDiscoveryProjection.Get(ProjectionId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            var changeWindow = existing.RefreshedAt;
            var documentChangesTask = HasDocumentChangesSinceAsync(changeWindow, cancellationToken);
            var insightChangesTask = HasInsightChangesSinceAsync(changeWindow, cancellationToken);
            var queueChangesTask = DocumentProcessingJobQueries.HasChangesSinceAsync(changeWindow, cancellationToken);

            await Task.WhenAll(documentChangesTask, insightChangesTask, queueChangesTask).ConfigureAwait(false);

            var hasChanges = documentChangesTask.Result || insightChangesTask.Result || queueChangesTask.Result;
            if (!hasChanges)
            {
                existing.Queue.AsOf = now;
                if ((now - existing.RefreshedAt) < MinimumRefreshWriteInterval)
                {
                    return existing;
                }

                existing.RefreshedAt = now;
                existing.RefreshDurationSeconds ??= 0d;
                return await SaveProjectionAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var documents = (await SourceDocument.All(cancellationToken).ConfigureAwait(false)).ToList();
        var insights = (await DocumentInsight.All(cancellationToken).ConfigureAwait(false)).ToList();

        var overview = BuildOverview(documents, insights);
        var collections = BuildCollections(documents, insights);
        var feed = BuildFeed(documents);
        var queue = await BuildQueueAsync(now, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        if (existing is not null && IsProjectionUnchanged(existing, overview, collections, feed, queue))
        {
            existing.RefreshedAt = now;
            existing.RefreshDurationSeconds = stopwatch.Elapsed.TotalSeconds;
            existing.Queue = queue;
            return await SaveProjectionAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        var projection = existing ?? new DocumentDiscoveryProjection();
        projection.RefreshedAt = now;
        projection.RefreshDurationSeconds = stopwatch.Elapsed.TotalSeconds;
        projection.Overview = overview;
        projection.Collections = collections;
        projection.Feed = feed;
        projection.Queue = queue;

        return await SaveProjectionAsync(projection, cancellationToken).ConfigureAwait(false);
    }

    private static DocumentInsightsOverview BuildOverview(IReadOnlyCollection<SourceDocument> documents, IReadOnlyCollection<DocumentInsight> insights)
    {
        var completed = documents.Count(d => d.Status == DocumentProcessingStatus.Completed);
        var active = documents.Count(d => d.Status is DocumentProcessingStatus.Queued or DocumentProcessingStatus.Extracting or DocumentProcessingStatus.Analyzing);
        var failed = documents.Count(d => d.Status == DocumentProcessingStatus.Failed);
        var confidenceValues = insights
            .Where(i => i.Confidence.HasValue)
            .Select(i => i.Confidence!.Value)
            .ToList();
        var averageConfidence = confidenceValues.Count == 0 ? 0d : Math.Round(confidenceValues.Average(), 3);

        var topProfiles = documents
            .Where(d => !string.IsNullOrWhiteSpace(d.AssignedProfileId))
            .GroupBy(d => d.AssignedProfileId!)
            .Select(group => new DocumentProfileUsage
            {
                ProfileId = group.Key,
                DocumentCount = group.Count(),
                LastUsed = group.Max(d => d.LastProcessedAt ?? d.UploadedAt)
            })
            .OrderByDescending(p => p.DocumentCount)
            .Take(5)
            .ToList();

        var recentDocuments = documents
            .OrderByDescending(d => d.LastProcessedAt ?? d.UploadedAt)
            .Take(8)
            .Select(d => new RecentDocumentInsight
            {
                DocumentId = d.Id,
                FileName = d.DisplayName ?? d.FileName,
                Status = d.Status,
                LastProcessedAt = d.LastProcessedAt ?? d.UploadedAt,
                AssignedProfileId = d.AssignedProfileId
            })
            .ToList();

        return new DocumentInsightsOverview
        {
            TotalDocuments = documents.Count,
            CompletedDocuments = completed,
            ActiveDocuments = active,
            FailedDocuments = failed,
            AverageConfidence = Math.Round(averageConfidence, 3),
            TopProfiles = topProfiles,
            RecentDocuments = recentDocuments
        };
    }

    private static List<DocumentCollectionSummary> BuildCollections(IReadOnlyCollection<SourceDocument> documents, IReadOnlyCollection<DocumentInsight> insights)
    {
        if (documents.Count == 0)
        {
            return new List<DocumentCollectionSummary>();
        }

        var insightsByDocument = insights
            .Where(i => i.SourceDocumentId != Guid.Empty)
            .GroupBy(i => i.SourceDocumentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return documents
            .GroupBy(d => string.IsNullOrWhiteSpace(d.AssignedProfileId) ? "unassigned" : d.AssignedProfileId!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var documentIds = group
                    .Select(d => Guid.TryParse(d.Id, out var parsed) ? parsed : (Guid?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToArray();

                var confidenceValues = documentIds
                    .SelectMany<Guid, DocumentInsight>(id => insightsByDocument.TryGetValue(id, out var list) ? list : Array.Empty<DocumentInsight>())
                    .Where(insight => insight.Confidence.HasValue)
                    .Select(insight => insight.Confidence!.Value)
                    .ToList();

                var latestDocuments = group
                    .OrderByDescending(d => d.LastProcessedAt ?? d.UploadedAt)
                    .Take(5)
                    .Select(d => new CollectionDocumentSummary
                    {
                        DocumentId = d.Id,
                        FileName = d.DisplayName ?? d.FileName,
                        Status = d.Status,
                        LastProcessedAt = d.LastProcessedAt ?? d.UploadedAt
                    })
                    .ToList();

                return new DocumentCollectionSummary
                {
                    ProfileId = group.Key,
                    DocumentCount = group.Count(),
                    AverageConfidence = confidenceValues.Count == 0 ? 0d : Math.Round(confidenceValues.Average(), 3),
                    LatestDocuments = latestDocuments
                };
            })
            .OrderByDescending(summary => summary.DocumentCount)
            .ToList();
    }

    private static List<AggregationFeedItem> BuildFeed(IReadOnlyCollection<SourceDocument> documents)
        => documents
            .OrderByDescending(d => d.LastProcessedAt ?? d.UploadedAt)
            .Take(20)
            .Select(document => new AggregationFeedItem
            {
                DocumentId = document.Id,
                FileName = document.DisplayName ?? document.FileName,
                Status = document.Status,
                Summary = document.Summary.PrimaryFindings ?? string.Empty,
                LastUpdated = document.LastProcessedAt ?? document.UploadedAt,
                InsightCount = document.Summary.InsightRefs.Count
            })
            .ToList();

    private static async Task<DocumentQueueProjection> BuildQueueAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var statuses = new[]
        {
            DocumentProcessingStatus.Queued,
            DocumentProcessingStatus.Extracting,
            DocumentProcessingStatus.Analyzing,
            DocumentProcessingStatus.Failed
        };

        var query = new DocumentProcessingJobQuery
        {
            Statuses = statuses,
            OrderByDue = true,
            Take = 20,
            IncludeExtraForPaging = true,
            DueBefore = asOf
        };

        var slice = await DocumentProcessingJobQueries.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        if (slice.Items.Count == 0)
        {
            return new DocumentQueueProjection
            {
                AsOf = asOf,
                PageSize = query.Take
            };
        }

        var ordered = slice.Items
            .OrderBy(job => job.NextAttemptAt ?? job.CreatedAt)
            .ThenBy(job => job.CreatedAt)
            .ToList();

        var documents = await GetDocumentsAsync(ordered.Select(job => job.SourceDocumentId), cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<Guid>();
        var entries = new List<DocumentQueueEntry>(ordered.Count);
        foreach (var job in ordered)
        {
            if (!seen.Add(job.SourceDocumentId))
            {
                continue;
            }

            documents.TryGetValue(job.SourceDocumentId, out var document);
            entries.Add(new DocumentQueueEntry
            {
                DocumentId = job.SourceDocumentId,
                FileName = document?.DisplayName ?? document?.FileName ?? job.SourceDocumentId.ToString(),
                Stage = job.Stage,
                Status = job.Status,
                EnqueuedAt = job.CreatedAt,
                NextAttemptAt = job.NextAttemptAt
            });
        }

        var oldest = slice.Items
            .Where(job => job.Status == DocumentProcessingStatus.Queued)
            .OrderBy(job => job.CreatedAt)
            .Select(job => (DateTimeOffset?)job.CreatedAt)
            .FirstOrDefault();

        return new DocumentQueueProjection
        {
            Pending = slice.Items.Count(job => job.Status == DocumentProcessingStatus.Queued),
            Failed = slice.Items.Count(job => job.Status == DocumentProcessingStatus.Failed),
            OldestQueuedAt = oldest,
            AsOf = asOf,
            HasMore = slice.HasMore,
            PageSize = query.Take,
            Entries = entries
        };
    }

    private static bool IsProjectionUnchanged(
        DocumentDiscoveryProjection existing,
        DocumentInsightsOverview overview,
        IReadOnlyCollection<DocumentCollectionSummary> collections,
        IReadOnlyCollection<AggregationFeedItem> feed,
        DocumentQueueProjection queue)
    {
        var existingPayload = SerializeProjection(existing.Overview, existing.Collections, existing.Feed, existing.Queue);
        var newPayload = SerializeProjection(overview, collections, feed, queue);
        return string.Equals(existingPayload, newPayload, StringComparison.Ordinal);
    }

    private static string SerializeProjection(
        DocumentInsightsOverview overview,
        IReadOnlyCollection<DocumentCollectionSummary> collections,
        IReadOnlyCollection<AggregationFeedItem> feed,
        DocumentQueueProjection queue)
        => JsonSerializer.Serialize(new
        {
            Overview = overview,
            Collections = collections,
            Feed = feed,
            Queue = queue
        }, SerializerOptions);

    private static async Task<DocumentDiscoveryProjection> SaveProjectionAsync(DocumentDiscoveryProjection projection, CancellationToken cancellationToken)
    {
        projection.Id = ProjectionId;
        projection.Scope = ProjectionId;
        projection.RefreshedAt = projection.RefreshedAt == default ? DateTimeOffset.UtcNow : projection.RefreshedAt;
        return await projection.Save(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasDocumentChangesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var threshold = since.UtcDateTime.ToString("O");
        var filter = $"UploadedAt > '{threshold}' || (LastProcessedAt != null && LastProcessedAt > '{threshold}')";
        var results = await SourceDocument.Query(filter, cancellationToken).ConfigureAwait(false);
        return results.Any();
    }

    private static async Task<bool> HasInsightChangesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var threshold = since.UtcDateTime.ToString("O");
        var filter = $"GeneratedAt > '{threshold}' || (UpdatedAt != null && UpdatedAt > '{threshold}')";
        var results = await DocumentInsight.Query(filter, cancellationToken).ConfigureAwait(false);
        return results.Any();
    }

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
}
