using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Events;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;
using Koan.Jobs.Queue;
using Koan.Jobs.RateGating;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Jobs.Execution;

/// <summary>
/// Runs one dispatch of a typed job (JOBS-0003). Generic on the concrete type, so it reads and
/// mutates the job's typed payload and shared runtime fields directly (no discriminator, no
/// reflection past the type-resolution the worker already did). Honours cancellation, the host rate
/// gate, typed WaitFor dependencies, the concurrency-lane permit (around the body only), and the
/// per-type retry policy. Deferrals re-enqueue with a future visible-at (JOBS-0002), never sleeping.
/// </summary>
internal static class JobDispatcher<T> where T : Job<T>, new()
{
    public static async Task Run(JobQueueItem item, IServiceProvider services, CancellationToken cancellationToken)
    {
        var queue = services.GetRequiredService<IJobQueue>();
        var rateGate = services.GetRequiredService<IHostRateGate>();
        var lanes = services.GetRequiredService<JobLaneRegistry>();
        var broker = services.GetRequiredService<JobProgressBroker>();
        var events = services.GetRequiredService<IJobEventPublisher>();
        var cancellations = services.GetRequiredService<JobCancellations>();
        var registry = services.GetRequiredService<JobTypeRegistry>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Jobs.Dispatcher")
            ?? (ILogger)NullLogger.Instance;

        var job = await Job<T>.Get(item.JobId, cancellationToken);
        if (job is null)
        {
            logger.LogWarning("Job {JobId} ({JobType}) not found in its set; dropping dispatch.", item.JobId, typeof(T).Name);
            return;
        }

        if (cancellations.IsRequested(job.Id))
        {
            await Finalize(job, JobStatus.Cancelled, null, events, cancellations, cancellationToken);
            return;
        }

        // Host rate gate: if the host is gated (typically a prior 429), defer without consuming a retry.
        var hostTag = job.HostTagInternal;
        if (hostTag is not null && rateGate.TryGetGate(hostTag, out var gate))
        {
            var releaseAt = gate.ReleaseAt <= DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow.AddSeconds(1) : gate.ReleaseAt;
            job.Status = JobStatus.Queued;
            job.QueuedAt = releaseAt;
            job.ProgressMessage = $"Waiting for host '{hostTag}' rate gate.";
            await job.SaveSelf(cancellationToken);
            await queue.Enqueue(new JobQueueItem(job.Id, typeof(T), item.Lane), releaseAt, cancellationToken);
            return;
        }

        // Typed WaitFor dependencies (ADR-0017): block until each referenced job is terminal.
        if (job.WaitForRefs.Count > 0)
        {
            var dep = await CheckDependencies(job, registry, cancellationToken);
            if (dep == DependencyOutcome.Poisoned)
            {
                await Finalize(job, JobStatus.Failed, "A declared dependency failed, was cancelled, or is missing.", events, cancellations, cancellationToken);
                return;
            }
            if (dep == DependencyOutcome.Blocked)
            {
                var backoff = ComputeBlockedBackoff(job);
                var until = DateTimeOffset.UtcNow.Add(backoff);
                job.Status = JobStatus.Blocked;
                job.QueuedAt = until;
                job.ProgressMessage = "Waiting on dependencies.";
                await job.SaveSelf(cancellationToken);
                await queue.Enqueue(new JobQueueItem(job.Id, typeof(T), item.Lane), until, cancellationToken);
                return;
            }
        }

        // Run.
        job.Attempt++;
        job.Status = JobStatus.Running;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.LastError = null;
        job.ProgressMessage = null;
        await job.SaveSelf(cancellationToken);
        await events.PublishStarted(job, cancellationToken);

        var tracker = new JobProgressTracker<T>(job, broker, cancellations, cancellationToken);
        JobExecutionOutcome outcome;
        // Lane permit wraps ONLY the job body (JOBS-0002), so deferrals never hold it.
        using (await lanes.AcquireAsync(item.Lane ?? job.LaneNameInternal, cancellationToken))
        {
            outcome = await Invoke(job, tracker, cancellationToken);
        }
        await tracker.Flush(cancellationToken);

        if (outcome.Status == JobExecutionStatus.Succeeded)
        {
            await Finalize(job, JobStatus.Completed, null, events, cancellations, cancellationToken);
            return;
        }
        if (outcome.Status == JobExecutionStatus.Cancelled)
        {
            await Finalize(job, JobStatus.Cancelled, null, events, cancellations, cancellationToken);
            return;
        }

        // Faulted: retry per policy, else fail.
        job.LastError = outcome.Error?.Message;
        var custom = job as ICustomRetryPolicy;
        var retry = job.RetryInternal;
        var error = outcome.Error ?? new Exception(job.LastError ?? "Unknown error");
        var shouldRetry = custom != null ? custom.ShouldRetry(job.Attempt, error) : job.Attempt < retry.MaxAttempts;

        if (!shouldRetry)
        {
            await Finalize(job, JobStatus.Failed, job.LastError, events, cancellations, cancellationToken);
            return;
        }

        TimeSpan delay;
        if (outcome.Error is RateLimitedJobException rl)
        {
            await rateGate.GateHost(rl.HostTag, rl.RetryAfter, rl.Message, cancellationToken);
            delay = rl.RetryAfter;
        }
        else
        {
            delay = custom != null ? custom.ComputeDelay(job.Attempt, error) : retry.ComputeDelay(job.Attempt);
        }

        var retryAt = DateTimeOffset.UtcNow.Add(delay);
        job.Status = JobStatus.Queued;
        job.QueuedAt = retryAt;
        await job.SaveSelf(cancellationToken);
        await events.PublishFailed(job, job.LastError, cancellationToken);
        await queue.Enqueue(new JobQueueItem(job.Id, typeof(T), item.Lane), retryAt, cancellationToken);
    }

    private static async Task<JobExecutionOutcome> Invoke(Job<T> job, JobProgressTracker<T> tracker, CancellationToken cancellationToken)
    {
        try
        {
            await job.InvokeDo(tracker, cancellationToken);
            return new JobExecutionOutcome(JobExecutionStatus.Succeeded, null);
        }
        catch (OperationCanceledException oce) when (tracker.CancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return new JobExecutionOutcome(JobExecutionStatus.Cancelled, oce);
        }
        catch (Exception ex)
        {
            return new JobExecutionOutcome(JobExecutionStatus.Faulted, ex);
        }
    }

    private static async Task Finalize(
        Job<T> job, JobStatus status, string? error,
        IJobEventPublisher events, JobCancellations cancellations, CancellationToken cancellationToken)
    {
        job.Status = status;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.Duration = job.CompletedAt - job.CreatedAt;
        if (error != null) job.LastError = error;
        await job.SaveSelf(cancellationToken);
        switch (status)
        {
            case JobStatus.Completed: await events.PublishCompleted(job, cancellationToken); break;
            case JobStatus.Failed: await events.PublishFailed(job, error, cancellationToken); break;
            case JobStatus.Cancelled: await events.PublishCancelled(job, cancellationToken); break;
        }
        cancellations.Clear(job.Id);
    }

    private enum DependencyOutcome { Ready, Blocked, Poisoned }

    private static async Task<DependencyOutcome> CheckDependencies(Job<T> job, JobTypeRegistry registry, CancellationToken cancellationToken)
    {
        foreach (var dep in job.WaitForRefs)
        {
            if (string.IsNullOrEmpty(dep.Id)) continue;
            var status = await registry.StatusOf(dep, cancellationToken);
            if (status is null) return DependencyOutcome.Poisoned;
            switch (status)
            {
                case JobStatus.Completed: continue;
                case JobStatus.Failed:
                case JobStatus.Cancelled: return DependencyOutcome.Poisoned;
                default: return DependencyOutcome.Blocked;
            }
        }
        return DependencyOutcome.Ready;
    }

    private static TimeSpan ComputeBlockedBackoff(Job<T> job)
    {
        const string counterKey = "blocked.attempt";
        var attempts = 0;
        if (job.Metadata.TryGetValue(counterKey, out var raw) && raw is not null)
            int.TryParse(raw.ToString(), out attempts);
        attempts++;
        job.Metadata[counterKey] = attempts;
        return TimeSpan.FromSeconds(Math.Min(5d * Math.Pow(2, attempts - 1), 300d));
    }
}
