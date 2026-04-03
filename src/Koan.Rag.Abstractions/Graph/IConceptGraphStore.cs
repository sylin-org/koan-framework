namespace Koan.Rag.Abstractions;

/// <summary>
/// Persistence adapter for the concept graph. First adapter: in-memory
/// with periodic snapshot. Second adapter (planned): SQLite via
/// <c>Koan.Cache.Adapter.Sqlite</c> patterns.
/// </summary>
public interface IConceptGraphStore
{
    /// <summary>Load graph state from persistent storage.</summary>
    Task Load(CancellationToken ct = default);

    /// <summary>Persist current graph state to storage.</summary>
    Task Save(CancellationToken ct = default);

    /// <summary>
    /// Retrieve the local subgraph around an entity, up to a given depth.
    /// Used by the concept_explore retrieval tool at query time.
    /// </summary>
    Task<GraphNeighborhood> GetNeighborhood(
        string entityId,
        int depth = 1,
        CancellationToken ct = default);

    /// <summary>Apply incremental changes (adds, updates, removals).</summary>
    Task ApplyDelta(GraphDelta delta, CancellationToken ct = default);

    /// <summary>Remove all entities, relationships, and mention data.</summary>
    Task Clear(CancellationToken ct = default);

    /// <summary>Current graph statistics.</summary>
    GraphStats GetStats();
}

/// <summary>
/// A local subgraph around a seed entity, returned by
/// <see cref="IConceptGraphStore.GetNeighborhood"/>.
/// </summary>
public sealed record GraphNeighborhood(
    IReadOnlyList<ConceptEntity> Entities,
    IReadOnlyList<ConceptRelationship> Relationships);

/// <summary>
/// Incremental changes to apply to the concept graph.
/// </summary>
public sealed record GraphDelta
{
    public IReadOnlyList<ConceptEntity> AddedEntities { get; init; } = [];
    public IReadOnlyList<ConceptRelationship> AddedRelationships { get; init; } = [];
    public IReadOnlyList<string> RemovedEntityIds { get; init; } = [];
    public IReadOnlyList<string> RemovedRelationshipIds { get; init; } = [];
    public IReadOnlyList<EntityMentionDelta> MentionDeltas { get; init; } = [];
}

/// <summary>
/// Tracks a mention count change for an entity from a specific document.
/// </summary>
public sealed record EntityMentionDelta(
    string EntityId,
    string DocumentId,
    int Delta);

/// <summary>Current graph size and health metrics.</summary>
public sealed record GraphStats(
    int EntityCount,
    int RelationshipCount,
    double Density,
    DateTimeOffset? LastPersisted);
