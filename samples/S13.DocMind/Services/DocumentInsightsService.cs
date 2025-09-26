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

public class DocumentInsightsService : IDocumentInsightsService
{
    private readonly IDocumentAggregationService _aggregationService;

    public DocumentInsightsService(IDocumentAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    public async Task<DocumentInsightsOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var files = await Models.File.All();
        var analyses = await Analysis.All();
        var documentTypes = await DocumentType.All();

        var completedAnalyses = analyses.Where(a => a.Status == "completed").ToList();
        var completedFiles = files.Where(f => f.Status == "completed").ToList();
        var activeDocuments = files.Where(f => f.Status is "processing" or "analyzing").ToList();

        var topDocumentTypes = files
            .Where(f => !string.IsNullOrEmpty(f.DocumentTypeId))
            .GroupBy(f => f.DocumentTypeId)
            .Select(group =>
            {
                var documentType = documentTypes.FirstOrDefault(dt => dt.Id == group.Key);
                return new DocumentTypeUsage
                {
                    DocumentTypeId = group.Key!,
                    DocumentTypeName = documentType?.Name ?? group.Key!,
                    UsageCount = group.Count(),
                    LastUsed = group.Max(f => f.CompletedDate ?? f.AssignedDate ?? f.UploadDate)
                };
            })
            .OrderByDescending(x => x.UsageCount)
            .Take(5)
            .ToList();

        var overallConfidence = completedAnalyses.Count == 0
            ? 0
            : completedAnalyses.Average(a => a.OverallConfidence);

        var processingRate = files.Count == 0
            ? 0
            : Math.Round(completedFiles.Count / (double)files.Count * 100, 1);

        var insight = new DocumentInsightsOverview
        {
            TotalDocuments = files.Count,
            CompletedDocuments = completedFiles.Count,
            ActiveDocuments = activeDocuments.Count,
            FailedDocuments = files.Count(f => f.Status == "failed"),
            AverageConfidence = Math.Round(overallConfidence, 3),
            AverageProcessingTimeMs = completedAnalyses
                .Where(a => a.ProcessingTimeMs.HasValue)
                .Select(a => a.ProcessingTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average(),
            HighQualityDocumentCount = completedAnalyses.Count(a => a.IsHighQuality),
            ReviewRequiredCount = completedAnalyses.Count(a => a.RequiresReview),
            TopDocumentTypes = topDocumentTypes,
            RecentDocuments = files
                .OrderByDescending(f => f.CompletedDate ?? f.AssignedDate ?? f.UploadDate)
                .Take(8)
                .Select(f => new RecentDocumentInsight
                {
                    FileId = f.Id!,
                    Name = f.Name,
                    Status = f.Status,
                    DocumentTypeId = f.DocumentTypeId,
                    CompletedDate = f.CompletedDate ?? f.AssignedDate ?? f.UploadDate,
                    Confidence = completedAnalyses
                        .FirstOrDefault(a => a.Id == f.AnalysisId)?.OverallConfidence
                })
                .ToList()
        };

        return insight;
    }

    public async Task<IReadOnlyCollection<DocumentCollectionSummary>> GetProfileCollectionsAsync(string profileId, CancellationToken cancellationToken)
    {
        var files = await Models.File.All();
        var analyses = await Analysis.All();
        var documentTypes = await DocumentType.All();

        var normalizedProfile = (profileId ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedProfile))
        {
            normalizedProfile = "all";
        }

        IEnumerable<IGrouping<string?, Models.File>> groupedFiles = normalizedProfile switch
        {
            "all" => files
                .Where(f => !string.IsNullOrEmpty(f.DocumentTypeId))
                .GroupBy(f => f.DocumentTypeId),
            _ => files
                .Where(f => string.Equals(
                    documentTypes.FirstOrDefault(dt => dt.Id == f.DocumentTypeId)?.Category,
                    normalizedProfile,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.DocumentTypeId)
        };

        var summaries = groupedFiles
            .Select(group =>
            {
                var documentType = documentTypes.FirstOrDefault(dt => dt.Id == group.Key);
                var latestFiles = group
                    .OrderByDescending(f => f.CompletedDate ?? f.AssignedDate ?? f.UploadDate)
                    .Take(5)
                    .Select(f => new CollectionDocumentSummary
                    {
                        FileId = f.Id!,
                        Name = f.Name,
                        Status = f.Status,
                        CompletedDate = f.CompletedDate,
                        AssignedDate = f.AssignedDate,
                        UploadDate = f.UploadDate
                    })
                    .ToList();

                var analysisConfidence = group
                    .Select(file => analyses.FirstOrDefault(a => a.Id == file.AnalysisId))
                    .Where(analysis => analysis != null && analysis.Status == "completed")
                    .Select(analysis => analysis!.OverallConfidence)
                    .DefaultIfEmpty(0)
                    .Average();

                return new DocumentCollectionSummary
                {
                    DocumentTypeId = group.Key ?? string.Empty,
                    DocumentTypeName = documentType?.Name ?? group.Key ?? "Unknown",
                    Category = documentType?.Category ?? normalizedProfile,
                    DocumentCount = group.Count(),
                    AverageConfidence = Math.Round(analysisConfidence, 3),
                    LatestDocuments = latestFiles
                };
            })
            .OrderByDescending(summary => summary.DocumentCount)
            .ToList();

        return summaries;
    }

    public Task<IReadOnlyCollection<AggregationFeedItem>> GetAggregationFeedAsync(CancellationToken cancellationToken)
        => _aggregationService.GetFeedAsync(cancellationToken);
}

public class DocumentAggregationService : IDocumentAggregationService
{
    public async Task<IReadOnlyCollection<AggregationFeedItem>> GetFeedAsync(CancellationToken cancellationToken)
    {
        var files = await Models.File.All();
        var analyses = await Analysis.All();
        var documentTypes = await DocumentType.All();

        var feed = files
            .OrderByDescending(f => f.CompletedDate ?? f.AssignedDate ?? f.UploadDate)
            .Take(20)
            .Select(file =>
            {
                var documentType = documentTypes.FirstOrDefault(dt => dt.Id == file.DocumentTypeId);
                var analysis = analyses.FirstOrDefault(a => a.Id == file.AnalysisId);
                var summary = analysis?.ProcessingMetadata.TryGetValue("summary", out var summaryValue) == true
                    ? summaryValue?.ToString()
                    : null;
                var highlights = analysis?.ProcessingMetadata.TryGetValue("highlights", out var highlightValue) == true
                    ? highlightValue as IEnumerable<string> ?? Array.Empty<string>()
                    : Array.Empty<string>();

                return new AggregationFeedItem
                {
                    FileId = file.Id!,
                    FileName = file.Name,
                    Status = file.Status,
                    DocumentTypeId = file.DocumentTypeId,
                    DocumentTypeName = documentType?.Name,
                    Confidence = analysis?.OverallConfidence,
                    Summary = summary ?? $"{file.Name} is {file.Status}",
                    LastUpdated = file.CompletedDate ?? file.AssignedDate ?? file.UploadDate,
                    Highlights = highlights.ToList()
                };
            })
            .ToList();

        return feed;
    }
}

public class DocumentInsightsOverview
{
    public int TotalDocuments { get; set; }
    public int CompletedDocuments { get; set; }
    public int ActiveDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public double AverageConfidence { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public int HighQualityDocumentCount { get; set; }
    public int ReviewRequiredCount { get; set; }
    public IReadOnlyCollection<DocumentTypeUsage> TopDocumentTypes { get; set; } = Array.Empty<DocumentTypeUsage>();
    public IReadOnlyCollection<RecentDocumentInsight> RecentDocuments { get; set; } = Array.Empty<RecentDocumentInsight>();
}

public class DocumentTypeUsage
{
    public string DocumentTypeId { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public DateTime? LastUsed { get; set; }
}

public class RecentDocumentInsight
{
    public string FileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DocumentTypeId { get; set; }
    public DateTime? CompletedDate { get; set; }
    public double? Confidence { get; set; }
}

public class DocumentCollectionSummary
{
    public string DocumentTypeId { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public double AverageConfidence { get; set; }
    public IReadOnlyCollection<CollectionDocumentSummary> LatestDocuments { get; set; } = Array.Empty<CollectionDocumentSummary>();
}

public class CollectionDocumentSummary
{
    public string FileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public DateTime? AssignedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
}

public class AggregationFeedItem
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DocumentTypeId { get; set; }
    public string? DocumentTypeName { get; set; }
    public double? Confidence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public IReadOnlyCollection<string> Highlights { get; set; } = Array.Empty<string>();
}
