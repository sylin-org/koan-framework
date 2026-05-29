using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Events;
using Koan.Jobs.Model;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Execution;

/// <summary>
/// Submits and cancels jobs (JOBS-0003). Submit resolves per-type policy (lane, coalesce key) from
/// the job instance, dedups against a live job when coalescing, persists the job to its own set, and
/// enqueues it (immediately or at a future visible-at for delayed start).
/// </summary>
internal sealed class JobCoordinator : IJobCoordinator
{
    private readonly IJobQueue _queue;
    private readonly IJobEventPublisher _events;
    private readonly JobCancellations _cancellations;
    private readonly ILogger<JobCoordinator> _logger;

    public JobCoordinator(IJobQueue queue, IJobEventPublisher events, JobCancellations cancellations, ILogger<JobCoordinator> logger)
    {
        _queue = queue;
        _events = events;
        _cancellations = cancellations;
        _logger = logger;
    }

    public async Task<T> Submit<T>(T job, TimeSpan? delay, CancellationToken cancellationToken)
        where T : Job<T>, new()
    {
        var lane = job.LaneNameInternal;
        var coalesceKey = job.CoalesceKeyInternal();

        // Coalesce-by-key (JOBS-0002): reuse a live job of the same type+key. Best-effort; handlers
        // are idempotent so a rare race that mints two is harmless.
        if (!string.IsNullOrWhiteSpace(coalesceKey))
        {
            var existing = await Job<T>.Query(
                j => j.CoalesceKey == coalesceKey
                     && j.Status != JobStatus.Completed
                     && j.Status != JobStatus.Failed
                     && j.Status != JobStatus.Cancelled,
                cancellationToken);
            if (existing.Count > 0)
            {
                _logger.LogDebug("Coalesced onto existing {JobType} job {JobId} (key {Key}).", typeof(T).Name, existing[0].Id, coalesceKey);
                return existing[0];
            }
        }

        var now = DateTimeOffset.UtcNow;
        var visibleAt = delay is { } d && d > TimeSpan.Zero ? now.Add(d) : now;

        job.Status = JobStatus.Queued;
        job.QueuedAt = visibleAt;
        job.CreatedAt = job.CreatedAt == default ? now : job.CreatedAt;
        job.CorrelationId ??= Activity.Current?.TraceId.ToString();
        job.ResolvedLane = lane;
        job.CoalesceKey = coalesceKey;

        await job.SaveSelf(cancellationToken);
        await _events.PublishQueued(job, cancellationToken);

        var item = new JobQueueItem(job.Id, typeof(T), lane);
        if (visibleAt > now) await _queue.Enqueue(item, visibleAt, cancellationToken);
        else await _queue.Enqueue(item, cancellationToken);

        _logger.LogDebug("Submitted {JobType} job {JobId} on lane {Lane}.", typeof(T).Name, job.Id, lane);
        return job;
    }

    public async Task Cancel<T>(string jobId, CancellationToken cancellationToken)
        where T : Job<T>, new()
    {
        _cancellations.Request(jobId);

        var job = await Job<T>.Get(jobId, cancellationToken);
        if (job is null) return;
        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled) return;

        // Not yet running: terminate immediately. Running jobs observe the cancellation flag at their
        // next checkpoint (the dispatcher/progress tracker).
        if (job.Status is JobStatus.Created or JobStatus.Queued or JobStatus.Blocked)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Duration = job.CompletedAt - job.CreatedAt;
            await job.SaveSelf(cancellationToken);
            await _events.PublishCancelled(job, cancellationToken);
        }
    }
}
