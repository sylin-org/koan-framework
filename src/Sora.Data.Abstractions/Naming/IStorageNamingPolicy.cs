namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Contract for adapters to implement naming derivation.
/// </summary>
public interface IStorageNamingPolicy
{
    StorageResolvedName Resolve(Type entityType);
}