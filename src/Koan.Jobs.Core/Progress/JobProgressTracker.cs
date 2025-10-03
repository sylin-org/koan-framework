using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Store;

namespace Koan.Jobs.Progress;

internal sealed class JobProgressTracker : IJobProgress
{
    private readonly Job _job;
    private readonly IJobStore _store;
    private readonly JobStoreMetadata _metadata;
    private readonly JobProgressBroker _broker;
    private readonly CancellationToken _cancellationToken;
    private readonly ConcurrentBag<Task> _pending = new();

    internal JobProgressTracker(Job job, IJobStore store, JobStoreMetadata metadata, JobProgressBroker broker, CancellationToken cancellationToken)
    {
        _job = job;
        _store = store;
        _metadata = metadata;
        _broker = broker;
        _cancellationToken = cancellationToken;
    }

    public DateTimeOffset? EstimatedCompletion => _job.EstimatedCompletion;

    public bool CancellationRequested => _job.CancellationRequested;

    public void Report(double percentage, string? message = null, DateTimeOffset? estimatedCompletion = null)
    {
        _job.Progress = Math.Clamp(percentage, 0d, 1d);
        if (message != null)
            _job.ProgressMessage = message;
        if (estimatedCompletion.HasValue)
            _job.EstimatedCompletion = estimatedCompletion;
        _job.UpdatedAt = DateTimeOffset.UtcNow;
        SchedulePersistence();
    }

    public void Report(int current, int total, string? message = null, DateTimeOffset? estimatedCompletion = null)
    {
        _job.CurrentStep = current;
        _job.TotalSteps = total;
        if (total > 0)
        {
            var ratio = Math.Clamp((double)current / total, 0d, 1d);
            _job.Progress = ratio;
        }
        if (message != null)
            _job.ProgressMessage = message;
        if (estimatedCompletion.HasValue)
            _job.EstimatedCompletion = estimatedCompletion;
        _job.UpdatedAt = DateTimeOffset.UtcNow;
        SchedulePersistence();
    }

    internal Task FlushAsync(CancellationToken cancellationToken)
    {
        var tasks = _pending.ToArray();
        if (tasks.Length == 0)
            return Task.CompletedTask;
        return Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private void SchedulePersistence()
    {
        var updateTask = PersistAsync();
        _pending.Add(updateTask);
        updateTask.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                // swallow but observe
                _ = t.Exception;
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task PersistAsync()
    {
        try
        {
            await _store.UpdateAsync(_job, _metadata, _cancellationToken).ConfigureAwait(false);
            await _broker.PublishAsync(_job, _cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }
}
