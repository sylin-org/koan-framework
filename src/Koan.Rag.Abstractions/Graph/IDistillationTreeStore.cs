namespace Koan.Rag.Abstractions;

/// <summary>
/// Persistence adapter for the RAPTOR distillation tree. Stores hierarchical
/// cluster summaries at multiple levels of abstraction. Mirrors
/// <see cref="IConceptGraphStore"/> in its Load/Save/Delta/Clear lifecycle.
/// </summary>
public interface IDistillationTreeStore
{
    /// <summary>Load tree state from persistent storage.</summary>
    Task Load(CancellationToken ct = default);

    /// <summary>Persist current tree state to storage.</summary>
    Task Save(CancellationToken ct = default);

    /// <summary>Retrieve a specific tree node by ID.</summary>
    Task<DistillationNode?> GetNode(string nodeId, CancellationToken ct = default);

    /// <summary>Retrieve all nodes at a specific tree level.</summary>
    Task<IReadOnlyList<DistillationNode>> GetLevel(int level, CancellationToken ct = default);

    /// <summary>Retrieve all nodes across all levels.</summary>
    Task<IReadOnlyList<DistillationNode>> GetAllNodes(CancellationToken ct = default);

    /// <summary>Apply incremental changes (adds, removals).</summary>
    Task ApplyDelta(TreeDelta delta, CancellationToken ct = default);

    /// <summary>Remove all tree nodes.</summary>
    Task Clear(CancellationToken ct = default);

    /// <summary>Current tree statistics.</summary>
    DistillationTreeStats GetStats();
}

/// <summary>
/// A node in the RAPTOR distillation tree. Each node summarizes a cluster
/// of child nodes (or leaf chunks at Level 1).
/// </summary>
public sealed record DistillationNode
{
    /// <summary>Stable unique identifier (e.g., "raptor:L2:cluster-47").</summary>
    public required string Id { get; init; }

    /// <summary>Tree level. Level 1 = summaries of leaf chunks. Higher = more abstract.</summary>
    public required int Level { get; init; }

    /// <summary>LLM-generated summary of the cluster's content.</summary>
    public required string Summary { get; init; }

    /// <summary>Embedding of the summary text.</summary>
    public required float[] Embedding { get; init; }

    /// <summary>IDs of child nodes (leaf chunk IDs at Level 1, tree node IDs at Level 2+).</summary>
    public IReadOnlyList<string> ChildNodeIds { get; init; } = [];

    /// <summary>Source document IDs that contributed to this cluster.</summary>
    public IReadOnlyList<string> SourceDocumentIds { get; init; } = [];

    /// <summary>Build version for atomic tree swaps.</summary>
    public long CorpusVersion { get; init; }
}

/// <summary>
/// Incremental changes to apply to the distillation tree.
/// </summary>
public sealed record TreeDelta
{
    public IReadOnlyList<DistillationNode> AddedNodes { get; init; } = [];
    public IReadOnlyList<string> RemovedNodeIds { get; init; } = [];
}

/// <summary>
/// Statistics for the distillation tree.
/// </summary>
public sealed record DistillationTreeStats(
    int TotalNodes,
    int TreeDepth,
    long CurrentVersion,
    DateTimeOffset? LastBuildTime);
