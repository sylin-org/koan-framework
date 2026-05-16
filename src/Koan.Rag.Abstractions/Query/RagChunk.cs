namespace Koan.Rag.Abstractions;

/// <summary>
/// A retrieved chunk from a RAG corpus with relevance scoring and provenance.
/// </summary>
public sealed record RagChunk(
    string ChunkId,
    string DocumentId,
    string Text,
    double Score,
    string? DocumentTitle = null,
    string? SectionTitle = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
