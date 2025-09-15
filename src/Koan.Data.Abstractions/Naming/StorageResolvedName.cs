namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Resolved storage name components.
/// For relational: Namespace = schema, Name = table. For document: Namespace = database (optional), Name = collection.
/// </summary>
public readonly record struct StorageResolvedName(string Name, string? Namespace = null);