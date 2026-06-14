namespace Koan.Rag.Abstractions;

/// <summary>
/// A concept entity extracted from document content. Represents a named concept,
/// object, standard, rule, or any other noun that documents reference.
/// </summary>
public sealed record ConceptEntity
{
    /// <summary>Stable unique identifier (deterministic from canonical name).</summary>
    public required string Id { get; init; }

    /// <summary>Canonical name after entity resolution.</summary>
    public required string CanonicalName { get; init; }

    /// <summary>One-line description of the entity in corpus context.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Embedding of the entity name + description for similarity-based resolution.
    /// <para>
    /// Note: the <c>Embedding</c> array uses reference equality in record comparisons.
    /// Do not use this type as a dictionary key or in HashSet operations.
    /// </para>
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>
    /// Number of documents that mention this entity. Used for garbage collection:
    /// entities reaching zero mentions are pruned.
    /// </summary>
    public int MentionCount { get; init; }

    /// <summary>
    /// Surface forms observed in source documents (e.g., "Fire-type", "Fire type",
    /// "fire type" all map to canonical "Fire Type"). Preserved for merge-on-read.
    /// </summary>
    public IReadOnlyList<string> SurfaceForms { get; init; } = [];
}
