using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Jobs.Model;
using Koan.Jobs.Support;

namespace Koan.Jobs.Store;

internal sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentDictionary<string, List<JobExecution>> _executions = new();
    private readonly JobIndexCache _index;

    public InMemoryJobStore(JobIndexCache index)
    {
        _index = index;
    }

    public Task<Job> CreateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        // Store in memory - no database persistence
        job.LastModified = DateTimeOffset.UtcNow;
        _jobs[job.Id] = job;
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.InMemory, null, null, metadata.Audit, job.GetType()));
        return Task.FromResult(job);
    }

    public Task<Job?> GetAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task<Job> UpdateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        // Update in memory - no database persistence
        job.LastModified = DateTimeOffset.UtcNow;
        _jobs[job.Id] = job;
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

    internal Task SweepAsync(TimeSpan completedRetention, TimeSpan faultedRetention, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Sweep in-memory jobs only - no database queries
        var jobsToRemove = _jobs.Values
            .Where(job => job.CompletedAt.HasValue &&
                ((job.Status == JobStatus.Completed && completedRetention > TimeSpan.Zero && now - job.CompletedAt >= completedRetention) ||
                 (job.Status == JobStatus.Failed && faultedRetention > TimeSpan.Zero && now - job.CompletedAt >= faultedRetention)))
            .ToList();

        // Remove from in-memory storage
        foreach (var job in jobsToRemove)
        {
            _jobs.TryRemove(job.Id, out _);
            _executions.TryRemove(job.Id, out _);
            _index.Remove(job.Id);
        }

        return Task.CompletedTask;
    }
}
