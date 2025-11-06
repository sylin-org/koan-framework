namespace Koan.Data.Vector.Abstractions.Partition;

/// <summary>
/// Maps partition IDs to storage-specific naming conventions
/// </summary>
/// <remarks>
/// Different vector stores have different naming requirements:
/// - Weaviate: Classes must be PascalCase, alphanumeric + underscore
/// - Pinecone: Namespaces use lowercase with hyphens
/// - Qdrant: Collections use lowercase with underscores
///
/// This abstraction allows each connector to implement its own mapping rules.
/// </remarks>
public interface IVectorPartitionMapper
{
    /// <summary>
    /// Maps a partition ID to a storage-specific collection/class name
    /// </summary>
    /// <typeparam name="T">Entity type being stored</typeparam>
    /// <param name="partitionId">Partition identifier (e.g., project ID, tenant ID)</param>
    /// <returns>Storage-specific name suitable for the vector provider</returns>
    string MapStorageName<T>(string partitionId);

    /// <summary>
    /// Sanitizes a partition ID according to provider rules
    /// </summary>
    /// <param name="partitionId">Raw partition ID</param>
    /// <returns>Sanitized version safe for storage provider</returns>
    string SanitizePartitionId(string partitionId);

    /// <summary>
    /// Gets the base collection/class name for an entity type
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <returns>Base name for the entity type</returns>
    string GetBaseName<T>();
}
