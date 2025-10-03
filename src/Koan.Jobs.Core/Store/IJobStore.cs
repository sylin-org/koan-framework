using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal interface IJobStore
{
    Task<Job> CreateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<Job?> GetAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<Job> UpdateAsync(Job job, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task RemoveAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);

    Task<JobExecution> CreateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<JobExecution> UpdateExecutionAsync(JobExecution execution, JobStoreMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobExecution>> ListExecutionsAsync(string jobId, JobStoreMetadata metadata, CancellationToken cancellationToken);
}
