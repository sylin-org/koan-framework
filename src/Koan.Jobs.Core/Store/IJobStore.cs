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

    Task<JobExecution> CreateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<JobExecution> UpdateExecution(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobExecution>> ListExecutions(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);
}
