using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class ProcessingJob : Entity<ProcessingJob>
{
    private static readonly TimeSpan HeartbeatGracePeriod = TimeSpan.FromMinutes(5);

    public string PipelineId { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int RetryCount { get; set; } = 0;

    public string? WorkerId { get; set; } = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; } = null;
    public DateTime? HeartbeatAt { get; set; } = null;
    public DateTime? CompletedAt { get; set; } = null;

    public string? LastError { get; set; } = null;

    public List<string> DocumentIds { get; set; } = new();
    public List<string> FieldPaths { get; set; } = new();
    public int TotalDocuments { get; set; } = 0;
    public int ProcessedDocuments { get; set; } = 0;
    public string? LastDocumentId { get; set; } = null;

    public double Progress
    {
        get
        {
            if (TotalDocuments <= 0)
            {
                return 0;
            }

            var ratio = (double)ProcessedDocuments / TotalDocuments;
            if (ratio < 0)
            {
                return 0;
            }

            return ratio > 1 ? 1 : ratio;
        }
    }

    public int ProgressPercent => (int)Math.Round(Progress * 100, MidpointRounding.AwayFromZero);

    public static async Task<ProcessingJob?> FindPendingAsync(string pipelineId, CancellationToken ct)
    {
        var pending = await Query(j =>
            j.PipelineId == pipelineId &&
            j.Status == JobStatus.Pending, ct).ConfigureAwait(false);

        return pending
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefault();
    }

    public static async Task<(ProcessingJob? Job, bool Cancelled)> TryCancelPendingAsync(string jobId, CancellationToken ct)
    {
        var job = await Get(jobId, ct).ConfigureAwait(false);
        if (job is null)
        {
            return (null, false);
        }

        var now = DateTime.UtcNow;
        if (job.Status != JobStatus.Pending)
        {
            var staleCutoff = now - HeartbeatGracePeriod;
            var isStaleProcessing = job.Status == JobStatus.Processing &&
                                    (job.HeartbeatAt is null || job.HeartbeatAt <= staleCutoff);

            if (!isStaleProcessing)
            {
                return (job, false);
            }
        }

        job.Status = JobStatus.Cancelled;
        job.CompletedAt = now;
        job.HeartbeatAt = now;
        job.WorkerId = null;
        job.LastError ??= "Cancelled";
        await job.Save(ct).ConfigureAwait(false);

        return (job, true);
    }

    public static async Task<ProcessingJob?> TryClaimAsync(string pipelineId, string workerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now - HeartbeatGracePeriod;

        var pending = await Query(j =>
            j.PipelineId == pipelineId &&
            (j.Status == JobStatus.Pending ||
                (j.Status == JobStatus.Processing && j.HeartbeatAt != null && j.HeartbeatAt < staleCutoff)), ct);

        var job = pending
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefault();

        if (job is null)
        {
            return null;
        }

        job.Status = JobStatus.Processing;
        job.WorkerId = workerId;
        job.ClaimedAt = now;
        job.HeartbeatAt = now;
        job.ProcessedDocuments = 0;
        job.LastDocumentId = null;
        job.LastError = null;
        await job.Save(ct).ConfigureAwait(false);
        return job;
    }

    public static async Task<ProcessingJob?> TryClaimAnyAsync(string workerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now - HeartbeatGracePeriod;

        var pending = await Query(j =>
            j.Status == JobStatus.Pending ||
            (j.Status == JobStatus.Processing && j.HeartbeatAt != null && j.HeartbeatAt < staleCutoff), ct);

        var job = pending
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefault();

        if (job is null)
        {
            return null;
        }

        job.Status = JobStatus.Processing;
        job.WorkerId = workerId;
        job.ClaimedAt = now;
        job.HeartbeatAt = now;
        job.ProcessedDocuments = 0;
        job.LastDocumentId = null;
        job.LastError = null;
        await job.Save(ct);
        return job;
    }

    public static async Task SignalHeartbeatAsync(string jobId, CancellationToken ct)
    {
        var job = await Get(jobId, ct);
        if (job is null)
        {
            return;
        }

        job.HeartbeatAt = DateTime.UtcNow;
        await job.Save(ct).ConfigureAwait(false);
    }

    public bool MergeDocuments(IEnumerable<string> documentIds)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        var existing = new HashSet<string>(DocumentIds, StringComparer.Ordinal);
        var added = false;

        foreach (var id in documentIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (existing.Add(id))
            {
                DocumentIds.Add(id);
                added = true;
            }
        }

        if (added)
        {
            TotalDocuments = DocumentIds.Count;
        }

        return added;
    }
}

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled
}
