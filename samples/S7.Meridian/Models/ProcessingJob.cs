using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class ProcessingJob : Entity<ProcessingJob>
{
    public string PipelineId { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int RetryCount { get; set; }
        = 0;

    public string? WorkerId { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }
        = null;
    public DateTime? HeartbeatAt { get; set; }
        = null;
    public DateTime? CompletedAt { get; set; }
        = null;

    public string? LastError { get; set; }
        = null;

    public List<string> DocumentIds { get; set; } = new();
    public List<string> FieldPaths { get; set; } = new();

    public static async Task<ProcessingJob?> TryClaimAsync(string pipelineId, string workerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now.AddMinutes(-5);

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
        job.LastError = null;
        await job.Save(ct);
        return job;
    }

    public static async Task<ProcessingJob?> TryClaimAnyAsync(string workerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now.AddMinutes(-5);

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
        await job.Save(ct);
    }
}

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
