using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Events;
using Koan.Jobs.Infrastructure;
using Koan.Jobs.Model;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;
using Koan.Jobs.Queue;
using Koan.Jobs.RateGating;
using Koan.Jobs.Store;
using Koan.Jobs.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

internal sealed class JobExecutor
{
    /// <summary>Convention key on <see cref="Job.Metadata"/> for the host-rate-gate concept.</summary>
    internal const string HostMetadataKey = "host";

    private readonly IJobStoreResolver _resolver;
    private readonly JobIndexCache _index;
    private readonly IJobEventPublisher _eventPublisher;
    private readonly JobProgressBroker _progressBroker;
    private readonly IJobQueue _queue;
    private readonly IHostRateGate _rateGate;
    private readonly JobsOptions _options;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IJobStoreResolver resolver,
        JobIndexCache index,
        IJobEventPublisher eventPublisher,
        JobProgressBroker progressBroker,
        IJobQueue queue,
        IHostRateGate rateGate,
        IOptions<JobsOptions> options,
        ILogger<JobExecutor> logger)
    {
        _resolver = resolver;
        _index = index;
        _eventPublisher = eventPublisher;
        _progressBroker = progressBroker;
        _queue = queue;
        _rateGate = rateGate;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(JobQueueItem item, CancellationToken cancellationToken)
    {
        using var activity = JobsTelemetry.Source.StartActivity("job.execute", ActivityKind.Internal);
        activity?.SetTag("job.id", item.JobId);
        activity?.SetTag("job.type", item.JobType.Name);
        activity?.SetTag("job.storage_mode", item.StorageMode.ToString());

        var metadata = new JobStoreMetadata(item.StorageMode, item.Source, item.Partition, item.AuditExecutions, _options.SerializerOptions);
        var store = _resolver.Resolve(item.StorageMode);
        var job = await store.Get(item.JobId, metadata, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found in store {StorageMode}", item.JobId, item.StorageMode);
            activity?.SetStatus(ActivityStatusCode.Error, "Job not found");
            return;
        }

        activity?.SetTag("job.name", job.Name);

        if (!_index.TryGet(job.Id, out var entry))
        {
            entry = new JobIndexEntry(job.Id, item.StorageMode, item.Source, item.Partition, item.AuditExecutions, item.JobType);
            _index.Set(entry);
        }

        if (entry.CancellationRequested)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt ??= DateTimeOffset.UtcNow;
            job.Duration = job.CompletedAt - job.CreatedAt;
            await store.Update(job, metadata, cancellationToken);
            await _eventPublisher.PublishCancelled(job, cancellationToken);
            activity?.SetTag("job.status", "Cancelled");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return;
        }

        var customRetryPolicy = job as ICustomRetryPolicy;
        var descriptor = ResolveRetryPolicy(item.JobType);
        var maxAttempts = customRetryPolicy != null ? int.MaxValue : descriptor.MaxAttempts;
        var attempt = await DetermineStartingAttempt(store, metadata, job.Id, item.AuditExecutions, cancellationToken);

        // Host-rate-gate check (see IHostRateGate documentation). If another job from the same host
        // is currently gated (typically because it hit a 429), don't even attempt this one — re-queue
        // until the gate releases. No retry budget is consumed; the worker re-picks the queued item
        // and re-runs this check.
        var hostTag = ResolveHostTag(job);
        if (hostTag is not null && _rateGate.TryGetGate(hostTag, out var activeGate))
        {
            job.Status = JobStatus.Queued;
            job.QueuedAt = activeGate.ReleaseAt;
            job.ProgressMessage = $"Waiting for host '{hostTag}' rate gate: {activeGate.Reason}";
            await store.Update(job, metadata, cancellationToken);
            activity?.SetTag("job.status", "RateGated");
            activity?.SetTag("job.host", hostTag);

            var waitFor = activeGate.ReleaseAt - DateTimeOffset.UtcNow;
            if (waitFor < TimeSpan.Zero) waitFor = TimeSpan.FromSeconds(1);

            _logger.LogInformation(
                "Job {JobId} deferred — host '{HostTag}' gated until {ReleaseAt} ({Reason}).",
                job.Id, hostTag, activeGate.ReleaseAt, activeGate.Reason);

            try { await Task.Delay(waitFor, cancellationToken); }
            catch (OperationCanceledException) { return; }

            await _queue.Enqueue(item, cancellationToken);
            return;
        }

        // Cross-job WaitFor check (ADR-0017). A dependent job whose declared prerequisites
        // haven't all cleared transitions to Blocked and re-queues with a geometric backoff.
        // Specific-id dependencies that ended Failed/Cancelled poison the dependent.
        var depCheck = await CheckDependencies(job, store, metadata, cancellationToken);
        if (depCheck.Outcome != DependencyOutcome.Ready)
        {
            if (depCheck.Outcome == DependencyOutcome.Poisoned)
            {
                job.Status = JobStatus.Failed;
                job.LastError = depCheck.Reason;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.Update(job, metadata, cancellationToken);
                await _eventPublisher.PublishFailed(job, depCheck.Reason, cancellationToken);
                activity?.SetTag("job.status", "DependencyPoisoned");
                activity?.SetStatus(ActivityStatusCode.Error, depCheck.Reason);
                _logger.LogWarning("Job {JobId} failed due to dependency: {Reason}.", job.Id, depCheck.Reason);
                return;
            }

            // Blocked — re-queue with backoff. Same shape as the host-rate-gate path above.
            var backoff = ComputeBlockedBackoff(job);
            job.Status = JobStatus.Blocked;
            job.QueuedAt = DateTimeOffset.UtcNow.Add(backoff);
            job.ProgressMessage = depCheck.Reason;
            await store.Update(job, metadata, cancellationToken);
            activity?.SetTag("job.status", "Blocked");

            _logger.LogDebug("Job {JobId} blocked: {Reason}. Re-queue in {Backoff:F0}s.",
                job.Id, depCheck.Reason, backoff.TotalSeconds);

            try { await Task.Delay(backoff, cancellationToken); }
            catch (OperationCanceledException) { return; }

            await _queue.Enqueue(item, cancellationToken);
            return;
        }

        while (attempt < maxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            activity?.SetTag("job.attempt", attempt);

            var execution = new JobExecution
            {
                JobId = job.Id,
                AttemptNumber = attempt,
                StartedAt = DateTimeOffset.UtcNow,
                Status = JobExecutionStatus.Running
            };

            if (item.AuditExecutions)
            {
                await store.CreateExecution(execution, metadata, cancellationToken);
            }

            job.Status = JobStatus.Running;
            job.StartedAt ??= execution.StartedAt;
            job.QueuedAt ??= job.CreatedAt;
            job.ProgressMessage = null;
            job.LastError = null;

            await store.Update(job, metadata, cancellationToken);
            await _eventPublisher.PublishStarted(job, cancellationToken);

            var tracker = new JobProgressTracker(job, store, metadata, _progressBroker, _index, cancellationToken);
            var outcome = await InvokeRunner(item, job, tracker, cancellationToken);
            await tracker.Flush(cancellationToken);

            if (item.AuditExecutions)
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Duration = execution.CompletedAt - execution.StartedAt;
                execution.Status = outcome.Status;
                execution.ErrorMessage = outcome.Error?.Message;
                execution.StackTrace = outcome.Error?.StackTrace;
                execution.Metrics["attempt"] = attempt;
                await store.UpdateExecution(execution, metadata, cancellationToken);
            }

            if (outcome.Status == JobExecutionStatus.Succeeded)
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.Update(job, metadata, cancellationToken);
                await _eventPublisher.PublishCompleted(job, cancellationToken);
                activity?.SetTag("job.status", "Completed");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return;
            }

            if (outcome.Status == JobExecutionStatus.Cancelled)
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.Update(job, metadata, cancellationToken);
                await _eventPublisher.PublishCancelled(job, cancellationToken);
                activity?.SetTag("job.status", "Cancelled");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return;
            }

            job.LastError = outcome.Error?.Message;

            if (outcome.Error != null)
            {
                activity?.AddEvent(new ActivityEvent("job.exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", outcome.Error.GetType().FullName },
                    { "exception.message", outcome.Error.Message }
                }));
            }

            var shouldRetry = customRetryPolicy != null
                ? customRetryPolicy.ShouldRetry(attempt, outcome.Error ?? new Exception(outcome.Error?.Message ?? "Unknown error"))
                : attempt < descriptor.MaxAttempts;

            if (!shouldRetry)
            {
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.Update(job, metadata, cancellationToken);
                await _eventPublisher.PublishFailed(job, outcome.Error?.Message, cancellationToken);
                activity?.SetTag("job.status", "Failed");
                activity?.SetStatus(ActivityStatusCode.Error, outcome.Error?.Message ?? "Job failed");
                return;
            }

            // RateLimitedJobException → set the cross-job gate AND override the per-job retry delay
            // with the rate-limit's Retry-After value. Other jobs targeting the same host will see
            // the gate at dispatch start and defer without consuming their retry budgets.
            TimeSpan delay;
            if (outcome.Error is RateLimitedJobException rateLimited)
            {
                await _rateGate.GateHost(rateLimited.HostTag, rateLimited.RetryAfter, rateLimited.Message, cancellationToken);
                delay = rateLimited.RetryAfter;
                _logger.LogWarning(
                    "Job {JobId} hit rate limit on host '{HostTag}'. Gating for {Duration:F0}s.",
                    job.Id, rateLimited.HostTag, rateLimited.RetryAfter.TotalSeconds);
            }
            else
            {
                delay = customRetryPolicy != null
                    ? customRetryPolicy.ComputeDelay(attempt, outcome.Error ?? new Exception(outcome.Error?.Message ?? "Unknown error"))
                    : descriptor.ComputeDelay(attempt);
            }

            job.Status = JobStatus.Queued;
            job.QueuedAt = DateTimeOffset.UtcNow.Add(delay);
            await store.Update(job, metadata, cancellationToken);
            await _eventPublisher.PublishFailed(job, outcome.Error?.Message, cancellationToken);

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _logger.LogInformation("Re-queueing job {JobId} after attempt {Attempt}", job.Id, attempt);
            await _queue.Enqueue(item, cancellationToken);
            return;
        }
    }

    private static RetryPolicyDescriptor ResolveRetryPolicy(Type jobType)
    {
        var attribute = jobType.GetCustomAttribute<RetryPolicyAttribute>(inherit: true);
        return attribute != null ? RetryPolicyDescriptor.FromAttribute(attribute) : RetryPolicyDescriptor.None;
    }

    /// <summary>
    /// Reads the host tag from <see cref="Job.Metadata"/> under the conventional key
    /// (<see cref="HostMetadataKey"/>). Returns <see langword="null"/> when not set — jobs without
    /// a declared host bypass the rate-gate path entirely.
    /// </summary>
    private static string? ResolveHostTag(Job job)
    {
        if (!job.Metadata.TryGetValue(HostMetadataKey, out var raw) || raw is null) return null;
        var value = raw.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static async Task<JobExecutionOutcome> InvokeRunner(JobQueueItem item, Job job, JobProgressTracker tracker, CancellationToken cancellationToken)
    {
        var runnerType = typeof(JobRunner<,,>).MakeGenericType(item.JobType, item.ContextType, item.ResultType);
        var method = runnerType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        var task = method?.Invoke(null, new object[] { job, tracker, cancellationToken }) as Task<JobExecutionOutcome>;
        if (task == null)
            throw new InvalidOperationException($"Unable to invoke JobRunner for {item.JobType.Name}.");
        return await task;
    }

    private static async Task<int> DetermineStartingAttempt(IJobStore store, JobStoreMetadata metadata, string jobId, bool auditEnabled, CancellationToken cancellationToken)
    {
        if (!auditEnabled)
            return 0;

        var executions = await store.ListExecutions(jobId, metadata, cancellationToken);
        return executions.Count;
    }

    /// <summary>
    /// Evaluates <see cref="Job.WaitForJobIds"/> and <see cref="Job.WaitForTypeNames"/> at dispatch
    /// time. Returns <see cref="DependencyOutcome.Ready"/> only when every specific id has reached
    /// <see cref="JobStatus.Completed"/> AND every type has at least one Completed instance.
    /// See ADR-0017.
    /// </summary>
    private static async Task<DependencyCheck> CheckDependencies(
        Job job,
        IJobStore store,
        JobStoreMetadata metadata,
        CancellationToken cancellationToken)
    {
        // Specific-id dependencies: terminal-required, Failed/Cancelled poisons the dependent.
        foreach (var depId in job.WaitForJobIds)
        {
            if (string.IsNullOrWhiteSpace(depId)) continue;
            var dep = await store.Get(depId, metadata, cancellationToken);
            if (dep is null)
            {
                // Unknown reference — treat as poisoned. Caller bug; should surface loudly.
                return new DependencyCheck(DependencyOutcome.Poisoned,
                    $"Dependency job {depId} not found.");
            }
            switch (dep.Status)
            {
                case JobStatus.Completed:
                    continue;
                case JobStatus.Failed:
                case JobStatus.Cancelled:
                    return new DependencyCheck(DependencyOutcome.Poisoned,
                        $"Dependency {depId} ended {dep.Status}.");
                default:
                    return new DependencyCheck(DependencyOutcome.Blocked,
                        $"Waiting on job {depId} (currently {dep.Status}).");
            }
        }

        // Type-based dependencies: any Completed of EACH listed type satisfies.
        foreach (var typeName in job.WaitForTypeNames)
        {
            if (string.IsNullOrWhiteSpace(typeName)) continue;
            var any = await store.HasCompletedJobOfType(typeName, metadata, cancellationToken);
            if (!any)
            {
                return new DependencyCheck(DependencyOutcome.Blocked,
                    $"Waiting on first successful {ShortName(typeName)} run.");
            }
        }

        return new DependencyCheck(DependencyOutcome.Ready, null);
    }

    /// <summary>
    /// Geometric backoff for jobs in <see cref="JobStatus.Blocked"/>. Starts at 5s, doubles each
    /// re-check, caps at 5 min. Re-check count is tracked via <see cref="Job.Metadata"/> so the
    /// backoff curve survives across re-queue cycles without an extra schema field.
    /// </summary>
    private static TimeSpan ComputeBlockedBackoff(Job job)
    {
        const string CounterKey = "blocked.attempt";
        var attempts = 0;
        if (job.Metadata.TryGetValue(CounterKey, out var raw) && raw is not null)
        {
            int.TryParse(raw.ToString(), out attempts);
        }
        attempts++;
        job.Metadata[CounterKey] = attempts;

        var seconds = Math.Min(5d * Math.Pow(2, attempts - 1), 300d); // 5 → 10 → 20 → … cap 300
        return TimeSpan.FromSeconds(seconds);
    }

    private static string ShortName(string fullName)
    {
        var i = fullName.LastIndexOf('.');
        return i >= 0 && i < fullName.Length - 1 ? fullName[(i + 1)..] : fullName;
    }

    private enum DependencyOutcome { Ready, Blocked, Poisoned }

    private readonly record struct DependencyCheck(DependencyOutcome Outcome, string? Reason);
}
