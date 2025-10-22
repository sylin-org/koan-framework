using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal interface IJobStoreResolver
{
    IJobStore Resolve(JobStorageMode mode);
}
