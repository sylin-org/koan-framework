using System;

namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Default resolver that delegates to the framework's static resolution logic.
/// </summary>
public sealed class DefaultStorageNameResolver : IStorageNameResolver
{
    public string Resolve(Type entityType, StorageNameResolver.Convention defaults)
        => StorageNameResolver.Resolve(entityType, defaults);
}
