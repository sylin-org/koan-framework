using System;
using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal sealed class JobStoreResolver : IJobStoreResolver
{
    private readonly InMemoryJobStore _inMemory;
    private readonly EntityJobStore _entity;

    public JobStoreResolver(InMemoryJobStore inMemory, EntityJobStore entity)
    {
        _inMemory = inMemory;
        _entity = entity;
    }

    public IJobStore Resolve(JobStorageMode mode)
        => mode switch
        {
            JobStorageMode.InMemory => _inMemory,
            JobStorageMode.Entity => _entity,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown job storage mode")
        };
}
