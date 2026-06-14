namespace Koan.Rag.Abstractions;

/// <summary>
/// Clustering strategy for RAPTOR tree construction. Abstracted to allow
/// swapping algorithms (GMM, k-means, HDBSCAN) without changing the tree builder.
/// Default: diagonal GMM with UMAP dimensionality reduction and BIC for k selection.
/// </summary>
public interface IClusteringStrategy
{
    /// <summary>
    /// Cluster embeddings into groups for summarization.
    /// Supports soft clustering: an embedding can appear in multiple clusters.
    /// </summary>
    /// <param name="embeddings">Embedding vectors with their source IDs.</param>
    /// <param name="targetClusters">Approximate number of clusters. Implementations
    /// may adjust based on BIC, silhouette score, or other criteria.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cluster assignments with member IDs and centroids.</returns>
    Task<IReadOnlyList<ClusterAssignment>> Cluster(
        IReadOnlyList<EmbeddingWithId> embeddings,
        int targetClusters,
        CancellationToken ct = default);
}

/// <summary>
/// An embedding vector paired with its source chunk/node ID.
/// <para>
/// Note: the <c>Embedding</c> array uses reference equality in record comparisons.
/// Do not use this type as a dictionary key or in HashSet operations.
/// </para>
/// </summary>
public sealed record EmbeddingWithId(string Id, float[] Embedding);

/// <summary>A cluster produced by the clustering strategy.</summary>
public sealed record ClusterAssignment
{
    /// <summary>Cluster index.</summary>
    public required int ClusterId { get; init; }

    /// <summary>IDs of embeddings assigned to this cluster.</summary>
    public required IReadOnlyList<string> MemberIds { get; init; }

    /// <summary>Cluster centroid embedding.</summary>
    public required float[] Centroid { get; init; }
}
