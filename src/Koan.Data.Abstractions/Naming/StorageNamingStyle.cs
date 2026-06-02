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
    /// <summary>
    /// Use the entity CLR type name plus a short stable hash of its declaring namespace/type context
    /// (e.g., Todo_8b1e0d77). Stays short and identifier-limit-safe while remaining collision-resistant
    /// across namespaces — the right default for stores with tight identifier limits (e.g., PostgreSQL's
    /// 63-byte names), where <see cref="FullNamespace"/> would overflow and truncate.
    /// </summary>
    HashedNamespace = 2,
}