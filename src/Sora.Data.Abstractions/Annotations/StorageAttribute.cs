using System;

namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Declares the logical storage container for an entity across providers.
/// For relational, maps to schema/table; for document, to database/collection; for vector, to index/collection.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class StorageAttribute : Attribute
{
    /// <summary>
    /// Logical name of the storage (table/collection/index). If not provided, a naming strategy will derive it from the type name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional logical namespace (e.g., schema/database). Adapters may map this to the appropriate concept or ignore it.
    /// </summary>
    public string? Namespace { get; init; }
}
