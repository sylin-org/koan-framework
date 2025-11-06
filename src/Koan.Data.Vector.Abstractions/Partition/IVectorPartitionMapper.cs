namespace Koan.Data.Vector.Abstractions.Partition;

/// <summary>
/// Maps partition identifiers to provider-specific vector storage names.
/// Enables partition-aware vector operations across different vector providers.
/// </summary>
/// <remarks>
/// Different vector providers handle partitions differently:
/// <list type="bullet">
/// <item><description>Weaviate: Per-partition class names (e.g., "KoanDocument_project_a")</description></item>
/// <item><description>Pinecone: Namespace-based partitioning</description></item>
/// <item><description>Qdrant: Collection per partition</description></item>
/// <item><description>pgvector: Table or schema per partition</description></item>
/// </list>
/// Implementers should sanitize partition IDs to comply with provider naming rules.
/// </remarks>
public interface IVectorPartitionMapper
{
    /// <summary>
    /// Maps a partition identifier to a provider-specific storage name for the given entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being stored in the vector database.</typeparam>
    /// <param name="partitionId">
    /// Partition identifier (e.g., "project-koan-framework", "tenant-123").
    /// Must not be null or whitespace.
    /// </param>
    /// <returns>
    /// Provider-specific storage name (e.g., class name, collection name, namespace).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="partitionId"/> is null or whitespace.
    /// </exception>
    /// <remarks>
    /// Implementations must ensure returned names are:
    /// <list type="bullet">
    /// <item><description>Deterministic (same input always returns same output)</description></item>
    /// <item><description>Provider-compatible (sanitized for naming rules)</description></item>
    /// <item><description>Collision-free (different partitions get different names)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Weaviate mapper
    /// var storageName = mapper.MapStorageName&lt;Document&gt;("project-koan-framework");
    /// // Returns: "KoanDocument_project_koan_framework"
    /// </code>
    /// </example>
    string MapStorageName<TEntity>(string partitionId) where TEntity : class;

    /// <summary>
    /// Sanitizes a partition identifier to comply with provider naming constraints.
    /// </summary>
    /// <param name="partitionId">Raw partition identifier.</param>
    /// <returns>Sanitized partition identifier safe for use in storage names.</returns>
    /// <remarks>
    /// Common sanitization rules:
    /// <list type="bullet">
    /// <item><description>Convert to lowercase</description></item>
    /// <item><description>Replace invalid characters with underscores or hyphens</description></item>
    /// <item><description>Enforce maximum length limits</description></item>
    /// <item><description>Remove leading/trailing special characters</description></item>
    /// </list>
    /// </remarks>
    string SanitizePartitionId(string partitionId);
}
