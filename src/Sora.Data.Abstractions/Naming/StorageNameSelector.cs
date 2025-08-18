using System;

namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Centralized helper to select the storage name using a consistent precedence:
/// adapter override -> repository-level override -> DI resolver.
/// </summary>
public static class StorageNameSelector
{
    public static string ResolveName(
        object? repository,
        IStorageNameResolver diResolver,
        Type entityType,
        StorageNameResolver.Convention defaults,
        Func<Type, string?>? adapterOverride = null)
    {
        // 1) Adapter-provided override
        var fromAdapter = adapterOverride?.Invoke(entityType);
        if (!string.IsNullOrWhiteSpace(fromAdapter)) return fromAdapter!;

        // 2) Repository-specific override (if implemented)
        if (repository is IStorageNameResolver custom)
        {
            var name = custom.Resolve(entityType, defaults);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        // 3) Default DI resolver
        return diResolver.Resolve(entityType, defaults);
    }
}
