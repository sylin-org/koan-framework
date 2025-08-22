namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Default naming styles for storage objects derived from an entity type.
/// </summary>
public enum StorageNamingStyle
{
    /// <summary>Use the entity CLR type name only (e.g., Todo).</summary>
    EntityType = 0,
    /// <summary>Use the full namespace-qualified name (e.g., My.App.Todo).</summary>
    FullNamespace = 1,
}

/// <summary>
/// Resolved storage name components.
/// For relational: Namespace = schema, Name = table. For document: Namespace = database (optional), Name = collection.
/// </summary>
public readonly record struct StorageResolvedName(string Name, string? Namespace = null);

/// <summary>
/// Contract for adapters to implement naming derivation.
/// </summary>
public interface IStorageNamingPolicy
{
    StorageResolvedName Resolve(Type entityType);
}
