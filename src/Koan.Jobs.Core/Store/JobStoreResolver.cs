using System;
using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal sealed class JobStoreResolver(InMemoryJobStore inMemory, EntityJobStore entity) : IJobStoreResolver
{
    public IJobStore Resolve(JobStorageMode mode)
        => mode switch
        {
            JobStorageMode.InMemory => inMemory,
            JobStorageMode.Entity => entity,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown job storage mode")
        };
}
