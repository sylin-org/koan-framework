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
        var query = await DocumentProcessingJob
            .Query($"SourceDocumentId == '{documentId}'", cancellationToken)
            .ConfigureAwait(false);

        return query.FirstOrDefault();
    }

    public static async Task<IReadOnlyList<DocumentProcessingJob>> GetPendingAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
    {
        var all = await DocumentProcessingJob.All(cancellationToken).ConfigureAwait(false);
        var pending = all
            .Where(job => job.Status == DocumentProcessingStatus.Queued && (job.NextAttemptAt is null || job.NextAttemptAt <= now))
            .OrderBy(job => job.NextAttemptAt ?? job.CreatedAt)
            .ThenBy(job => job.CreatedAt)
            .Take(Math.Max(1, batchSize))
            .ToList();

        return pending;
    }

    public static async Task<IReadOnlyList<DocumentProcessingJob>> GetAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await DocumentProcessingJob.All(cancellationToken).ConfigureAwait(false);
        return jobs.ToList();
    }
}
