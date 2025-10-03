namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Represents a single exported vector with its ID, embedding, and metadata.
/// Returned in batches by ExportAllAsync for memory-efficient streaming.
/// </summary>
/// <typeparam name="TKey">Type of entity identifier</typeparam>
/// <param name="Id">Entity ID this vector belongs to</param>
/// <param name="Embedding">The embedding vector (dense float array)</param>
/// <param name="Metadata">Optional metadata stored with the vector</param>
public sealed record VectorExportBatch<TKey>(
    TKey Id,
    float[] Embedding,
    object? Metadata = null
) where TKey : notnull;
