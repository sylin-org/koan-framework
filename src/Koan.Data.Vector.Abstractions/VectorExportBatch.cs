namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Represents a single exported vector with its ID, embedding, and metadata.
/// Returned in batches by ExportAllAsync for memory-efficient streaming.
/// </summary>
/// <typeparam name="TKey">Type of entity identifier</typeparam>
public sealed record VectorExportBatch<TKey>(
    /// <summary>Entity ID this vector belongs to</summary>
    TKey Id,

    /// <summary>The embedding vector (dense float array)</summary>
    float[] Embedding,

    /// <summary>Optional metadata stored with the vector</summary>
    object? Metadata = null
) where TKey : notnull;
