using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentIngestionService
{
    Task<IReadOnlyList<SourceDocument>> IngestAsync(string pipelineId, IFormFileCollection files, CancellationToken ct);
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

    public async Task<IReadOnlyList<SourceDocument>> IngestAsync(string pipelineId, IFormFileCollection files, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("At least one file is required for ingestion.", nameof(files));
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {pipelineId} not found.");

        var savedDocuments = new List<SourceDocument>(capacity: files.Count);

        foreach (var file in files)
        {
            if (file is null || file.Length == 0)
            {
                _logger.LogWarning("Skipping empty upload for pipeline {PipelineId}.", pipeline.Id);
                continue;
            }

            var document = await IngestInternalAsync(pipeline, file, ct).ConfigureAwait(false);
            savedDocuments.Add(document);
        }

        if (savedDocuments.Count == 0)
        {
            throw new InvalidOperationException("No valid files were provided for ingestion.");
        }

        pipeline.TotalDocuments += savedDocuments.Count;
        if (pipeline.Status == PipelineStatus.Pending)
        {
            pipeline.Status = PipelineStatus.Queued;
        }

        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct).ConfigureAwait(false);

        return savedDocuments;
    }

    public async Task<SourceDocument> IngestAsync(string pipelineId, IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        var results = await IngestAsync(pipelineId, new FormFileCollection { file }, ct).ConfigureAwait(false);
        return results[0];
    }

    private async Task<SourceDocument> IngestInternalAsync(DocumentPipeline pipeline, IFormFile file, CancellationToken ct)
    {
        await _validator.ValidateAsync(file, ct).ConfigureAwait(false);

        await using var stream = file.OpenReadStream();
        var storageKey = await _storage.StoreAsync(stream, file.FileName, file.ContentType, ct).ConfigureAwait(false);

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

        await document.Save(ct).ConfigureAwait(false);

        _logger.LogInformation("Stored document {DocumentId} for pipeline {PipelineId}.", document.Id, pipeline.Id);
        return document;
    }
}
