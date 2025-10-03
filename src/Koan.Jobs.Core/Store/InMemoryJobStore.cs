using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Support;

namespace Koan.Jobs.Store;

internal sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<string, StoredJob> _jobs = new();
    private readonly ConcurrentDictionary<string, List<JobExecution>> _executions = new();
    private readonly JobIndexCache _index;

    public InMemoryJobStore(JobIndexCache index)
    {
        _index = index;
    }

    public Task<Job> CreateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        var stored = _jobs.GetOrAdd(job.Id, _ => new StoredJob(job));
        stored.Update(job);
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.InMemory, null, null, metadata.Audit, job.GetType()));
        return Task.FromResult(job);
    }

    public Task<Job?> GetAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        return Task.FromResult(_jobs.TryGetValue(jobId, out var stored) ? stored.Job : null);
    }

    public Task<Job> UpdateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        var stored = _jobs.GetOrAdd(job.Id, _ => new StoredJob(job));
        stored.Update(job);
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.InMemory, null, null, metadata.Audit, job.GetType()));
        return Task.FromResult(job);
    }

    public Task RemoveAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        _jobs.TryRemove(jobId, out _);
        _executions.TryRemove(jobId, out _);
        _index.Remove(jobId);
        return Task.CompletedTask;
    }

    public Task<JobExecution> CreateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        var list = _executions.GetOrAdd(execution.JobId, _ => new List<JobExecution>());
        lock (list)
        {
            list.Add(execution);
        }
        return Task.FromResult(execution);
    }

    public Task<JobExecution> UpdateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        var list = _executions.GetOrAdd(execution.JobId, _ => new List<JobExecution>());
        lock (list)
        {
            var index = list.FindIndex(e => e.AttemptNumber == execution.AttemptNumber);
            if (index >= 0)
            {
                list[index] = execution;
            }
            else
            {
                list.Add(execution);
            }
        }
        return Task.FromResult(execution);
    }

    public Task<IReadOnlyList<JobExecution>> ListExecutionsAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        if (_executions.TryGetValue(jobId, out var list))
        {
            lock (list)
            {
                return Task.FromResult((IReadOnlyList<JobExecution>)list.ToArray());
            }
        }
        return Task.FromResult((IReadOnlyList<JobExecution>)Array.Empty<JobExecution>());
    }

    internal void Sweep(TimeSpan completedRetention, TimeSpan faultedRetention)
    {
        var now = DateTimeOffset.UtcNow;
        RemoveIf(job =>
            job.CompletedAt.HasValue &&
            ((job.Status == JobStatus.Completed && completedRetention > TimeSpan.Zero && now - job.CompletedAt >= completedRetention) ||
             (job.Status == JobStatus.Failed && faultedRetention > TimeSpan.Zero && now - job.CompletedAt >= faultedRetention)));
    }

    private void RemoveIf(Func<Job, bool> predicate)
    {
        foreach (var pair in _jobs)
        {
            var job = pair.Value.Job;
            if (predicate(job))
            {
                _jobs.TryRemove(pair.Key, out _);
                _executions.TryRemove(pair.Key, out _);
                _index.Remove(pair.Key);
            }
        }
    }

    private sealed class StoredJob
    {
        private Job _job;

        internal StoredJob(Job job)
        {
            _job = job;
        }

        internal Job Job => _job;

        internal void Update(Job job)
        {
            _job = job;
        }
    }
}
