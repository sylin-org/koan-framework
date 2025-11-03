using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Infrastructure;

internal static class PipelineSnapshotMapper
{
    public static async Task<IReadOnlyList<JobSnapshot>> LoadJobSnapshotsAsync(string pipelineId, CancellationToken ct)
    {
        var jobs = await ProcessingJob.Query(job => job.PipelineId == pipelineId, ct);

        return jobs
            .OrderByDescending(job => job.CreatedAt)
            .Take(10)
            .Select(MapJob)
            .ToArray();
    }

    public static async Task<IReadOnlyList<RunLogSnapshot>> LoadRunLogSnapshotsAsync(string pipelineId, CancellationToken ct)
    {
        var logs = await RunLog.Query(log => log.PipelineId == pipelineId, ct);

        return logs
            .OrderByDescending(log => log.StartedAt)
            .Take(20)
            .Select(MapRunLog)
            .ToArray();
    }

    private static JobSnapshot MapJob(ProcessingJob job)
        => new()
        {
            Id = job.Id ?? string.Empty,
            Status = job.Status.ToString(),
            ProgressPercent = job.ProgressPercent,
            TotalDocuments = job.TotalDocuments,
            ProcessedDocuments = job.ProcessedDocuments,
            LastDocumentId = job.LastDocumentId,
            LastError = job.LastError,
            CreatedAt = job.CreatedAt,
            ClaimedAt = job.ClaimedAt,
            CompletedAt = job.CompletedAt
        };

    private static RunLogSnapshot MapRunLog(RunLog log)
        => new()
        {
            Id = log.Id ?? string.Empty,
            Stage = log.Stage,
            Status = log.Status,
            DocumentId = log.DocumentId,
            FieldPath = log.FieldPath,
            StartedAt = log.StartedAt,
            FinishedAt = log.FinishedAt,
            Metadata = log.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}
