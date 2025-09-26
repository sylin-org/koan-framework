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
    bool ContainsImages);

public sealed record ExtractedChunk(int Index, string Channel, string Content, string? Summary);
