using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentIngestionService
{
    Task<SourceDocument> IngestAsync(string pipelineId, IFormFile file, CancellationToken ct);
}

public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDocumentStorage _storage;
    private readonly ISecureUploadValidator _validator;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(IDocumentStorage storage, ISecureUploadValidator validator, ILogger<DocumentIngestionService> logger)
    {
        _storage = storage;
        _validator = validator;
        _logger = logger;
    }

    public async Task<SourceDocument> IngestAsync(string pipelineId, IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {pipelineId} not found.");

        await _validator.ValidateAsync(file, ct);

        await using var stream = file.OpenReadStream();
        var storageKey = await _storage.StoreAsync(stream, file.FileName, file.ContentType, ct);

        var document = new SourceDocument
        {
            PipelineId = pipeline.Id,
            OriginalFileName = file.FileName,
            StorageKey = storageKey,
            SourceType = MeridianConstants.SourceTypes.Unclassified,
            MediaType = file.ContentType,
            Size = file.Length,
            Status = DocumentProcessingStatus.Pending,
            UploadedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await document.Save(ct);

        _logger.LogInformation("Stored document {DocumentId} for pipeline {PipelineId}.", document.Id, pipeline.Id);
        return document;
    }
}
