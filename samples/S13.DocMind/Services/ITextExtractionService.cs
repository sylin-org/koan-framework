using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface ITextExtractionService
{
    Task<DocumentExtractionResult> ExtractAsync(SourceDocument document, CancellationToken cancellationToken);
}

public sealed record DocumentExtractionResult(
    string Text,
    IReadOnlyList<ExtractedChunk> Chunks,
    int WordCount,
    int PageCount,
    bool ContainsImages,
    IReadOnlyDictionary<string, object?> Diagnostics,
    string? Language = null);

public sealed record ExtractedChunk(int Index, string Content, string? Summary, IReadOnlyDictionary<string, object?> Metadata);
