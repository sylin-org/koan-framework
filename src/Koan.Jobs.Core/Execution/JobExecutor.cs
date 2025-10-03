using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Events;
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

    public async Task ExecuteAsync(JobQueueItem item, CancellationToken cancellationToken)
    {
        var metadata = new JobStoreMetadata(item.StorageMode, item.Source, item.Partition, item.AuditExecutions, _options.SerializerOptions);
        var store = _resolver.Resolve(item.StorageMode);
        var job = await store.GetAsync(item.JobId, metadata, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found in store {StorageMode}", item.JobId, item.StorageMode);
            return;
        }

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
            await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
            await _eventPublisher.PublishCancelledAsync(job, cancellationToken).ConfigureAwait(false);
            return;
        }

        var descriptor = ResolveRetryPolicy(item.JobType);
        var attempt = await DetermineStartingAttemptAsync(store, metadata, job.Id, item.AuditExecutions, cancellationToken).ConfigureAwait(false);

        while (attempt < descriptor.MaxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            var execution = new JobExecution
            {
                JobId = job.Id,
                AttemptNumber = attempt,
                StartedAt = DateTimeOffset.UtcNow,
                Status = JobExecutionStatus.Running
            };

            if (item.AuditExecutions)
            {
                await store.CreateExecutionAsync(execution, metadata, cancellationToken).ConfigureAwait(false);
            }

            job.Status = JobStatus.Running;
            job.StartedAt ??= execution.StartedAt;
            job.QueuedAt ??= job.CreatedAt;
            job.ProgressMessage = null;
            job.LastError = null;

            await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
            await _eventPublisher.PublishStartedAsync(job, cancellationToken).ConfigureAwait(false);

            var tracker = new JobProgressTracker(job, store, metadata, _progressBroker, _index, cancellationToken);
            var outcome = await InvokeRunner(item, job, tracker, cancellationToken).ConfigureAwait(false);
            await tracker.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (item.AuditExecutions)
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Duration = execution.CompletedAt - execution.StartedAt;
                execution.Status = outcome.Status;
                execution.ErrorMessage = outcome.Error?.Message;
                execution.StackTrace = outcome.Error?.StackTrace;
                execution.Metrics["attempt"] = attempt;
                await store.UpdateExecutionAsync(execution, metadata, cancellationToken).ConfigureAwait(false);
            }

            if (outcome.Status == JobExecutionStatus.Succeeded)
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
                await _eventPublisher.PublishCompletedAsync(job, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (outcome.Status == JobExecutionStatus.Cancelled)
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
                await _eventPublisher.PublishCancelledAsync(job, cancellationToken).ConfigureAwait(false);
                return;
            }

            job.LastError = outcome.Error?.Message;

            if (attempt >= descriptor.MaxAttempts)
            {
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Duration = job.CompletedAt - job.CreatedAt;
                await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
                await _eventPublisher.PublishFailedAsync(job, outcome.Error?.Message, cancellationToken).ConfigureAwait(false);
                return;
            }

            var delay = descriptor.ComputeDelay(attempt);
            job.Status = JobStatus.Queued;
            job.QueuedAt = DateTimeOffset.UtcNow.Add(delay);
            await store.UpdateAsync(job, metadata, cancellationToken).ConfigureAwait(false);
            await _eventPublisher.PublishFailedAsync(job, outcome.Error?.Message, cancellationToken).ConfigureAwait(false);

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _logger.LogInformation("Re-queueing job {JobId} after attempt {Attempt}", job.Id, attempt);
            await _queue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
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
        return await task.ConfigureAwait(false);
    }

    private static async Task<int> DetermineStartingAttemptAsync(IJobStore store, JobStoreMetadata metadata, string jobId, bool auditEnabled, CancellationToken cancellationToken)
    {
        if (!auditEnabled)
            return 0;

        var executions = await store.ListExecutionsAsync(jobId, metadata, cancellationToken).ConfigureAwait(false);
        return executions.Count;
    }
}
