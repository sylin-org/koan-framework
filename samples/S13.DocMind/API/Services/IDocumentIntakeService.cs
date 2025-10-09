using S13.DocMind.Contracts;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentIntakeService
{
    Task<DocumentUploadReceipt> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken);
    Task AssignProfileAsync(SourceDocument document, string profileId, bool acceptSuggestion, CancellationToken cancellationToken);
    Task RequeueAsync(string documentId, DocumentProcessingStage stage, CancellationToken cancellationToken);
}
