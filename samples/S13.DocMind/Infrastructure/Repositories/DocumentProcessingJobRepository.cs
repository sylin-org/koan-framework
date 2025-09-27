using System;
using System.Collections.Generic;
using System.Linq;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure.Repositories;

public static class DocumentProcessingJobRepository
{
    public static async Task<DocumentProcessingJob?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await DocumentProcessingJob.Get(id.ToString(), cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public static async Task<DocumentProcessingJob?> FindByDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var slice = await QueryAsync(new DocumentProcessingJobQuery
        {
            SourceDocumentId = documentId,
            Take = 1
        }, cancellationToken).ConfigureAwait(false);

        return slice.Items.FirstOrDefault();
    }

    public static Task<DocumentProcessingJobSlice> GetPendingAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
        => QueryAsync(new DocumentProcessingJobQuery
        {
            Statuses = new[] { DocumentProcessingStatus.Queued },
            DueBefore = now,
            Take = Math.Max(1, batchSize),
            OrderByDue = true
        }, cancellationToken);

    public static Task<DocumentProcessingJobSlice> QueryAsync(DocumentProcessingJobQuery query, CancellationToken cancellationToken)
    {
        var filter = BuildFilter(query);
        return ExecuteQueryAsync(filter, query, cancellationToken);
    }

    public static async Task<IReadOnlyList<DocumentProcessingJob>> GetAllAsync(CancellationToken cancellationToken)
    {
        var results = await DocumentProcessingJob.All(cancellationToken).ConfigureAwait(false);
        return results.ToList();
    }

    public static async Task<DocumentProcessingJobSlice> ExecuteQueryAsync(string? filter, DocumentProcessingJobQuery query, CancellationToken cancellationToken)
    {
        var result = filter is null
            ? await DocumentProcessingJob.All(cancellationToken).ConfigureAwait(false)
            : await DocumentProcessingJob.Query(filter, cancellationToken).ConfigureAwait(false);

        var ordered = query.OrderByDue
            ? result.OrderBy(job => job.NextAttemptAt ?? job.CreatedAt).ThenBy(job => job.CreatedAt)
            : result.OrderByDescending(job => job.UpdatedAt);

        var limit = query.Take > 0 ? query.Take + (query.IncludeExtraForPaging ? 1 : 0) : int.MaxValue;
        var materialized = ordered
            .Take(limit)
            .ToList();

        var hasMore = query.Take > 0 && query.IncludeExtraForPaging && materialized.Count > query.Take;
        if (hasMore)
        {
            materialized.RemoveAt(materialized.Count - 1);
        }

        return new DocumentProcessingJobSlice(materialized, hasMore);
    }

    private static string? BuildFilter(DocumentProcessingJobQuery query)
    {
        var filters = new List<string>();

        if (query.SourceDocumentId.HasValue)
        {
            filters.Add($"SourceDocumentId == '{query.SourceDocumentId.Value}'");
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            filters.Add($"CorrelationId == '{query.CorrelationId}'");
        }

        if (query.Statuses is { Length: > 0 })
        {
            var statusFilter = string.Join(" || ", query.Statuses.Select(status => $"Status == {(int)status}"));
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filters.Add($"({statusFilter})");
            }
        }

        if (query.Stages is { Length: > 0 })
        {
            var stageFilter = string.Join(" || ", query.Stages.Select(stage => $"Stage == {(int)stage}"));
            if (!string.IsNullOrWhiteSpace(stageFilter))
            {
                filters.Add($"({stageFilter})");
            }
        }

        if (query.DueBefore.HasValue)
        {
            var due = query.DueBefore.Value.UtcDateTime.ToString("O");
            filters.Add($"(NextAttemptAt == null || NextAttemptAt <= '{due}')");
        }

        return filters.Count == 0 ? null : string.Join(" && ", filters);
    }
}

public sealed record DocumentProcessingJobSlice(IReadOnlyList<DocumentProcessingJob> Items, bool HasMore);

public sealed class DocumentProcessingJobQuery
{
    public Guid? SourceDocumentId { get; set; }
        = null;

    public string? CorrelationId { get; set; }
        = null;

    public DocumentProcessingStatus[]? Statuses { get; set; }
        = null;

    public DocumentProcessingStage[]? Stages { get; set; }
        = null;

    public DateTimeOffset? DueBefore { get; set; }
        = null;

    public int Take { get; set; }
        = 0;

    public bool OrderByDue { get; set; }
        = false;

    public bool IncludeExtraForPaging { get; set; }
        = true;
}
