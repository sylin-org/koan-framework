using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Jobs.Model;
using Koan.Jobs.Support;

namespace Koan.Jobs.Store;

internal sealed class EntityJobStore(JobIndexCache index) : IJobStore
{

    public async Task<Job> Create(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await job.Save(cancellationToken);
        index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task<Job?> Get(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        var job = await Job.Get(jobId, cancellationToken);
        if (job != null)
        {
            index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        }
        return job;
    }

    public async Task<Job> Update(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await job.Save(cancellationToken);
        index.Set(new JobIndexEntry(job.Id, JobStorageMode.Entity, metadata.Source, metadata.Partition, metadata.Audit, job.GetType()));
        return job;
    }

    public async Task Remove(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await Job.Remove(jobId, cancellationToken);
        index.Remove(jobId);
    }

    public async Task<JobExecution> CreateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await execution.Save(cancellationToken);
        return execution;
    }

    public async Task<JobExecution> UpdateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken)
    {
        using var scope = EnterContext(metadata.Source, metadata.Partition);
        await execution.Save(cancellationToken);
        return execution;
    }

    public async Task<IReadOnlyList<JobExecution>> ListExecutions(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken)
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
