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
    private readonly ConcurrentDictionary<string, List<JobExecution>> _executions = new();
    private readonly JobIndexCache _index;

    public InMemoryJobStore(JobIndexCache index)
    {
        _index = index;
    }

    public async Task<Job> CreateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        // [Timestamp] auto-updated by RepositoryFacade
        await job.Save();
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.InMemory, null, null, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task<Job?> GetAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        return await Job.Get(jobId);
    }

    public async Task<Job> UpdateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        // [Timestamp] auto-updated by RepositoryFacade
        await job.Save();
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.InMemory, null, null, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task RemoveAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        await Data<Job, string>.DeleteAsync(jobId);
        _executions.TryRemove(jobId, out _);
        _index.Remove(jobId);
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

    internal async Task SweepAsync(TimeSpan completedRetention, TimeSpan faultedRetention, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Query for jobs to remove using LINQ predicate
        var jobsToRemove = await Job.Query(job =>
            job.CompletedAt.HasValue &&
            ((job.Status == JobStatus.Completed && completedRetention > TimeSpan.Zero && now - job.CompletedAt >= completedRetention) ||
             (job.Status == JobStatus.Failed && faultedRetention > TimeSpan.Zero && now - job.CompletedAt >= faultedRetention)));

        // Delete jobs
        foreach (var job in jobsToRemove)
        {
            await Data<Job, string>.DeleteAsync(job.Id, cancellationToken);
            _executions.TryRemove(job.Id, out _);
            _index.Remove(job.Id);
        }
    }
}
