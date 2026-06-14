namespace Koan.Rag.Abstractions;

/// <summary>
/// A source document that contributed to a RAG answer.
/// </summary>
public sealed record RagSource(
    string DocumentId,
    string? DocumentTitle,
    string? SectionTitle,
    double RelevanceScore,
    IReadOnlyList<string> ChunkIds);
