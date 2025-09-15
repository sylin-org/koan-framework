namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Contract for overriding storage name resolution.
/// Implementations can customize how table/collection names are derived.
/// </summary>
public interface IStorageNameResolver
{
    string Resolve(Type entityType, StorageNameResolver.Convention defaults);
}
