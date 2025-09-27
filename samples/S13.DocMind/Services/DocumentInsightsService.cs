using System;
using System.Collections.Generic;
using System.Linq;
using S13.DocMind.Infrastructure.Repositories;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentInsightsService
{
    Task<DocumentInsightsOverview> GetOverviewAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DocumentCollectionSummary>> GetProfileCollectionsAsync(string profileId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AggregationFeedItem>> GetAggregationFeedAsync(CancellationToken cancellationToken);
}

public sealed class DocumentInsightsService : IDocumentInsightsService
{
    private readonly TimeProvider _clock;

    public DocumentInsightsService(TimeProvider clock)
    {
        _clock = clock;
    }

    public async Task<DocumentInsightsOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var projection = await EnsureProjectionAsync(cancellationToken).ConfigureAwait(false);
        return projection.Overview;
    }

    public async Task<IReadOnlyCollection<DocumentCollectionSummary>> GetProfileCollectionsAsync(string profileId, CancellationToken cancellationToken)
    {
        var projection = await EnsureProjectionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return projection.Collections;
        }

        return projection.Collections
            .Where(summary => string.Equals(summary.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyCollection<AggregationFeedItem>> GetAggregationFeedAsync(CancellationToken cancellationToken)
    {
        var projection = await EnsureProjectionAsync(cancellationToken).ConfigureAwait(false);
        return projection.Feed;
    }

    private async Task<DocumentDiscoveryProjection> EnsureProjectionAsync(CancellationToken cancellationToken)
    {
        var projection = await DocumentDiscoveryProjectionRepository.GetAsync(cancellationToken).ConfigureAwait(false);
        if (projection is null || (_clock.GetUtcNow() - projection.RefreshedAt) > TimeSpan.FromMinutes(5))
        {
            projection = await DocumentDiscoveryProjectionBuilder.RefreshAsync(_clock, cancellationToken).ConfigureAwait(false);
        }

        return projection;
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
