using System;
using System.Collections.Generic;
using System.Linq;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentInsightsService
{
    Task<DocumentInsightsOverview> GetOverviewAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DocumentCollectionSummary>> GetProfileCollectionsAsync(string profileId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AggregationFeedItem>> GetAggregationFeedAsync(CancellationToken cancellationToken);
}

public interface IDocumentAggregationService
{
    Task<IReadOnlyCollection<AggregationFeedItem>> GetFeedAsync(CancellationToken cancellationToken);
}

public sealed class DocumentInsightsService : IDocumentInsightsService
{
    private readonly IDocumentAggregationService _aggregationService;

    public DocumentInsightsService(IDocumentAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    public async Task<DocumentInsightsOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var insights = await DocumentInsight.All(cancellationToken).ConfigureAwait(false);

        var completed = documents.Where(d => d.Status == DocumentProcessingStatus.Completed).ToList();
        var active = documents.Where(d => d.Status is DocumentProcessingStatus.Queued or DocumentProcessingStatus.Extracting or DocumentProcessingStatus.Analyzing).ToList();
        var failed = documents.Where(d => d.Status == DocumentProcessingStatus.Failed).ToList();

        var averageConfidence = insights
            .Where(i => i.Confidence.HasValue)
            .Select(i => i.Confidence!.Value)
            .DefaultIfEmpty(0d)
            .Average();

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
            CompletedDocuments = completed.Count,
            ActiveDocuments = active.Count,
            FailedDocuments = failed.Count,
            AverageConfidence = Math.Round(averageConfidence, 3),
            TopProfiles = topProfiles,
            RecentDocuments = recentDocuments
        };
    }

    public async Task<IReadOnlyCollection<DocumentCollectionSummary>> GetProfileCollectionsAsync(string profileId, CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var insights = await DocumentInsight.All(cancellationToken).ConfigureAwait(false);

        IEnumerable<IGrouping<string?, SourceDocument>> grouped = string.IsNullOrWhiteSpace(profileId)
            ? documents.GroupBy(d => d.AssignedProfileId)
            : documents.Where(d => string.Equals(d.AssignedProfileId, profileId, StringComparison.OrdinalIgnoreCase)).GroupBy(d => d.AssignedProfileId);

        return grouped
            .Select(group => new DocumentCollectionSummary
            {
                ProfileId = group.Key ?? "unassigned",
                DocumentCount = group.Count(),
                AverageConfidence = insights
                    .Where(i => group.Any(d => Guid.TryParse(d.Id, out var docId) && i.SourceDocumentId == docId) && i.Confidence.HasValue)
                    .Select(i => i.Confidence!.Value)
                    .DefaultIfEmpty(0d)
                    .Average(),
                LatestDocuments = group
                    .OrderByDescending(d => d.LastProcessedAt ?? d.UploadedAt)
                    .Take(5)
                    .Select(d => new CollectionDocumentSummary
                    {
                        DocumentId = d.Id,
                        FileName = d.DisplayName ?? d.FileName,
                        Status = d.Status,
                        LastProcessedAt = d.LastProcessedAt ?? d.UploadedAt
                    })
                    .ToList()
            })
            .OrderByDescending(summary => summary.DocumentCount)
            .ToList();
    }

    public Task<IReadOnlyCollection<AggregationFeedItem>> GetAggregationFeedAsync(CancellationToken cancellationToken)
        => _aggregationService.GetFeedAsync(cancellationToken);
}

public sealed class DocumentAggregationService : IDocumentAggregationService
{
    public async Task<IReadOnlyCollection<AggregationFeedItem>> GetFeedAsync(CancellationToken cancellationToken)
    {
        var documents = await SourceDocument.All(cancellationToken).ConfigureAwait(false);
        var insights = await DocumentInsight.All(cancellationToken).ConfigureAwait(false);

        return documents
            .OrderByDescending(d => d.LastProcessedAt ?? d.UploadedAt)
            .Take(20)
            .Select(document =>
            {
                var documentId = Guid.TryParse(document.Id, out var parsed) ? parsed : Guid.Empty;
                var relatedInsights = insights.Where(i => i.SourceDocumentId == documentId).ToList();
                return new AggregationFeedItem
                {
                    DocumentId = document.Id,
                    FileName = document.DisplayName ?? document.FileName,
                    Status = document.Status,
                    Summary = document.Summary.PrimaryFindings ?? string.Empty,
                    LastUpdated = document.LastProcessedAt ?? document.UploadedAt,
                    InsightCount = relatedInsights.Count
                };
            })
            .ToList();
    }
}

public sealed class DocumentInsightsOverview
{
    public int TotalDocuments { get; set; }
    public int CompletedDocuments { get; set; }
    public int ActiveDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public double AverageConfidence { get; set; }
    public IReadOnlyCollection<DocumentProfileUsage> TopProfiles { get; set; } = Array.Empty<DocumentProfileUsage>();
    public IReadOnlyCollection<RecentDocumentInsight> RecentDocuments { get; set; } = Array.Empty<RecentDocumentInsight>();
}

public sealed class DocumentProfileUsage
{
    public string ProfileId { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public DateTimeOffset? LastUsed { get; set; }
}

public sealed class RecentDocumentInsight
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DocumentProcessingStatus Status { get; set; }
    public DateTimeOffset LastProcessedAt { get; set; }
    public string? AssignedProfileId { get; set; }
}

public sealed class DocumentCollectionSummary
{
    public string ProfileId { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public double AverageConfidence { get; set; }
    public IReadOnlyCollection<CollectionDocumentSummary> LatestDocuments { get; set; } = Array.Empty<CollectionDocumentSummary>();
}

public sealed class CollectionDocumentSummary
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DocumentProcessingStatus Status { get; set; }
    public DateTimeOffset LastProcessedAt { get; set; }
}

public sealed class AggregationFeedItem
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DocumentProcessingStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
    public int InsightCount { get; set; }
}
