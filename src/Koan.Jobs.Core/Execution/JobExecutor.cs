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
using Koan.Jobs.Store;
using Koan.Jobs.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

internal sealed class JobExecutor
{
    private readonly IJobStoreResolver _resolver;
    private readonly JobIndexCache _index;
    private readonly IJobEventPublisher _eventPublisher;
    private readonly JobProgressBroker _progressBroker;
    private readonly IJobQueue _queue;
    private readonly JobsOptions _options;
    private readonly ILogger<JobExecutor> _logger;

    public JobExecutor(
        IJobStoreResolver resolver,
        JobIndexCache index,
        IJobEventPublisher eventPublisher,
        JobProgressBroker progressBroker,
        IJobQueue queue,
        IOptions<JobsOptions> options,
        ILogger<JobExecutor> logger)
    {
        _resolver = resolver;
        _index = index;
        _eventPublisher = eventPublisher;
        _progressBroker = progressBroker;
        _queue = queue;
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

            var delay = customRetryPolicy != null
                ? customRetryPolicy.ComputeDelay(attempt, outcome.Error ?? new Exception(outcome.Error?.Message ?? "Unknown error"))
                : descriptor.ComputeDelay(attempt);
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
}
