using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal interface IJobStore
{
    Task<Job> Create(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<Job?> Get(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<Job> Update(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task Remove(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when at least one job of the given <see cref="Job.TypeName"/> exists in the
    /// store with <see cref="JobStatus.Completed"/>. Used by the dependency check at dispatch
    /// time to satisfy <see cref="Job.WaitForTypeNames"/>. See ADR-0017.
    /// </summary>
    Task<bool> HasCompletedJobOfType(string typeName, JobStoreMetadata metadata, CancellationToken cancellationToken);

    Task<JobExecution> CreateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<JobExecution> UpdateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobExecution>> ListExecutions(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);
}
