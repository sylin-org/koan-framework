using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Progress;

internal sealed class JobProgressBroker
{
    private readonly ConcurrentDictionary<string, List<JobProgressSubscription>> _subscriptions = new();

    public IDisposable Subscribe(string jobId, Func<JobProgressUpdate, Task> handler, CancellationToken cancellationToken)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var subscription = new JobProgressSubscription(jobId, handler, this);
        var bucket = _subscriptions.GetOrAdd(jobId, _ => new List<JobProgressSubscription>());
        lock (bucket)
        {
            bucket.Add(subscription);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(subscription.Dispose);
        }

        return subscription;
    }

    public Task PublishAsync(Job job, CancellationToken cancellationToken)
    {
        var update = new JobProgressUpdate
        {
            Percentage = job.Progress,
            Message = job.ProgressMessage,
            EstimatedCompletion = job.EstimatedCompletion,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return PublishAsync(job.Id, update, cancellationToken);
    }

    public async Task PublishAsync(string jobId, JobProgressUpdate update, CancellationToken cancellationToken)
    {
        if (!_subscriptions.TryGetValue(jobId, out var bucket))
            return;

        JobProgressSubscription[] targets;
        lock (bucket)
        {
            targets = bucket.ToArray();
        }

        foreach (var subscription in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await subscription.InvokeAsync(update);
        }
    }

    internal void Remove(JobProgressSubscription subscription)
    {
        if (!_subscriptions.TryGetValue(subscription.JobId, out var bucket))
            return;

        lock (bucket)
        {
            bucket.Remove(subscription);
            if (bucket.Count == 0)
            {
                _subscriptions.TryRemove(subscription.JobId, out _);
            }
        }
    }
}

internal sealed class JobProgressSubscription : IDisposable
{
    private readonly Func<JobProgressUpdate, Task> _handler;
    private readonly JobProgressBroker _broker;
    private int _disposed;

    internal JobProgressSubscription(string jobId, Func<JobProgressUpdate, Task> handler, JobProgressBroker broker)
    {
        JobId = jobId;
        _handler = handler;
        _broker = broker;
    }

    internal string JobId { get; }

    internal Task InvokeAsync(JobProgressUpdate update)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return Task.CompletedTask;
        return _handler(update);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _broker.Remove(this);
        }
    }
}
