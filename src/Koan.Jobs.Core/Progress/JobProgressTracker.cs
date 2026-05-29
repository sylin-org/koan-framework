using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Execution;
using Koan.Jobs.Model;

namespace Koan.Jobs.Progress;

/// <summary>
/// <see cref="IJobProgress"/> handed to a running job (JOBS-0003). Mutates the typed job's fields,
/// persists them via the job's own set, and fans the update out to subscribers. Cancellation is
/// surfaced from the ephemeral <see cref="JobCancellations"/> registry.
/// </summary>
internal sealed class JobProgressTracker<T> : IJobProgress
    where T : Job<T>, new()
{
    private readonly Job<T> _job;
    private readonly JobProgressBroker _broker;
    private readonly JobCancellations _cancellations;
    private readonly CancellationToken _cancellationToken;
    private readonly ConcurrentBag<Task> _pending = new();

    internal JobProgressTracker(Job<T> job, JobProgressBroker broker, JobCancellations cancellations, CancellationToken cancellationToken)
    {
        _job = job;
        _broker = broker;
        _cancellations = cancellations;
        _cancellationToken = cancellationToken;
    }

    public DateTimeOffset? EstimatedCompletion => _job.EstimatedCompletion;

    public bool CancellationRequested => _cancellations.IsRequested(_job.Id);

    public void Report(double percentage, string? message = null, DateTimeOffset? estimatedCompletion = null)
    {
        _job.Progress = Math.Clamp(percentage, 0d, 1d);
        if (message != null) _job.ProgressMessage = message;
        if (estimatedCompletion.HasValue) _job.EstimatedCompletion = estimatedCompletion;
        Schedule();
    }

    public void Report(int current, int total, string? message = null, DateTimeOffset? estimatedCompletion = null)
    {
        _job.CurrentStep = current;
        _job.TotalSteps = total;
        if (total > 0) _job.Progress = Math.Clamp((double)current / total, 0d, 1d);
        if (message != null) _job.ProgressMessage = message;
        if (estimatedCompletion.HasValue) _job.EstimatedCompletion = estimatedCompletion;
        Schedule();
    }

    internal Task Flush(CancellationToken cancellationToken)
    {
        var tasks = _pending.ToArray();
        return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private void Schedule()
    {
        var task = Persist();
        _pending.Add(task);
        task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task Persist()
    {
        try
        {
            await _job.SaveSelf(_cancellationToken);
            await _broker.Publish(_job, _cancellationToken);
        }
        catch (OperationCanceledException) { /* ignore */ }
    }
}
