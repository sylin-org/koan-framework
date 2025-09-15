namespace Koan.Data.Abstractions.Naming;

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