using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Jobs.Model;
using Koan.Jobs.Support;

namespace Koan.Jobs.Store;

internal sealed class EntityJobStore : IJobStore
{
    private readonly JobIndexCache _index;

    public EntityJobStore(JobIndexCache index)
    {
        _index = index;
    }

    public async Task<Job> CreateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await job.Save(cancellationToken);
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task<Job?> GetAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        var job = await Job.Get(jobId, cancellationToken);
        if (job != null)
        {
            _index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        }
        return job;
    }

    public async Task<Job> UpdateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await job.Save(cancellationToken);
        _index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task RemoveAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await Job.Remove(jobId, cancellationToken);
        _index.Remove(jobId);
    }

    public async Task<JobExecution> CreateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await execution.Save(cancellationToken);
        return execution;
    }

    public async Task<JobExecution> UpdateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await execution.Save(cancellationToken);
        return execution;
    }

    public async Task<IReadOnlyList<JobExecution>> ListExecutionsAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        var executions = await JobExecution.Query(e => e.JobId == jobId, cancellationToken);
        return executions;
    }

    private static IDisposable EnterContext(string? source, string? partition)
    {
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(partition))
            return new NoopDisposable();

        return EntityContext.With(source: source, partition: partition);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
