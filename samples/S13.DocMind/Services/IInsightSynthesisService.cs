using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IInsightSynthesisService
{
    Task<InsightSynthesisResult> GenerateAsync(SourceDocument document, DocumentExtractionResult extraction, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}
