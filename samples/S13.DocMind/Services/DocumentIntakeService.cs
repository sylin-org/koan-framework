using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class DocumentIntakeService : IDocumentIntakeService
{
    private readonly IDocumentStorage _storage;
    private readonly DocumentPipelineQueue _queue;
    private readonly DocMindOptions _options;
    private readonly ILogger<DocumentIntakeService> _logger;
    private readonly TimeProvider _clock;

    public DocumentIntakeService(
        IDocumentStorage storage,
        DocumentPipelineQueue queue,
        IOptions<DocMindOptions> options,
        ILogger<DocumentIntakeService> logger,
        TimeProvider clock)
    {
        _storage = storage;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    public async Task<DocumentUploadReceipt> UploadAsync(UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.File is null) throw new ValidationException("File is required");

        if (request.File.Length <= 0)
        {
            throw new ValidationException("File is empty");
        }

        if (request.File.Length > _options.Storage.MaxFileSizeBytes)
        {
            throw new ValidationException($"File exceeds {_options.Storage.MaxFileSizeBytes / (1024 * 1024)} MB limit");
        }

        if (_options.Storage.AllowedContentTypes.Length > 0 &&
            !_options.Storage.AllowedContentTypes.Contains(request.File.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("File {File} uploaded with unsupported content type {ContentType}", request.File.FileName, request.File.ContentType);
        }

        await using var stream = request.File.OpenReadStream();
        var stored = await _storage.SaveAsync(request.File.FileName, stream, cancellationToken);

        var duplicate = await FindDuplicateAsync(stored.Hash, cancellationToken);
        if (duplicate is not null)
        {
            try
            {
                if (File.Exists(stored.Path))
                {
                    File.Delete(stored.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to delete duplicate file {Path}", stored.Path);
            }
            _logger.LogInformation("Detected duplicate upload {File} -> {DocumentId}", request.File.FileName, duplicate.Id);
            return new DocumentUploadReceipt
            {
                DocumentId = duplicate.Id,
                FileName = duplicate.OriginalFileName,
                Status = duplicate.Status,
                Duplicate = true,
                Hash = duplicate.Hash,
                Tags = new Dictionary<string, string>(duplicate.Tags, StringComparer.OrdinalIgnoreCase)
            };
        }

        var now = _clock.GetUtcNow();
        var document = new SourceDocument
        {
            FileName = Path.GetFileName(stored.Path),
            OriginalFileName = request.File.FileName,
            ContentType = request.File.ContentType,
            Length = stored.Length,
            Hash = stored.Hash,
            UploadedAt = now,
            Summary = new DocumentSummary
            {
                TextExtracted = false,
                WordCount = 0,
                PageCount = 0,
                ChunkCount = 0
            },
            Storage = new Models.StorageLocation
            {
                Provider = stored.Provider,
                Path = stored.Path,
                Hash = stored.Hash,
                Size = stored.Length
            }
        };

        if (request.Tags is { Count: > 0 })
        {
            foreach (var (key, value) in request.Tags)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                document.Tags[key] = value ?? string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileId))
        {
            document.AssignedProfileId = request.ProfileId;
            document.AssignedBySystem = false;
        }

        await document.Save(cancellationToken);

        await RecordEventAsync(document, DocumentProcessingStage.Upload, DocumentProcessingStatus.Uploaded,
            "Document uploaded",
            new Dictionary<string, string>
            {
                ["fileName"] = document.OriginalFileName,
                ["contentType"] = document.ContentType,
                ["length"] = stored.Length.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken);

        await _queue.EnqueueAsync(new DocumentWorkItem(document.Id, DocumentProcessingStage.Upload), cancellationToken);

        return new DocumentUploadReceipt
        {
            DocumentId = document.Id,
            FileName = document.OriginalFileName,
            Status = document.Status,
            Duplicate = false,
            Hash = document.Hash,
            Tags = new Dictionary<string, string>(document.Tags, StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task AssignProfileAsync(SourceDocument document, string profileId, bool acceptSuggestion, CancellationToken cancellationToken)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Profile id required", nameof(profileId));

        document.AssignedProfileId = profileId;
        document.AssignedBySystem = acceptSuggestion;
        document.UpdatedAt = _clock.GetUtcNow();
        await document.Save(cancellationToken);

        await RecordEventAsync(document, DocumentProcessingStage.Suggestion, DocumentProcessingStatus.Deduplicated,
            acceptSuggestion ? "Profile auto-accepted" : "Profile assignment queued",
            new Dictionary<string, string>
            {
                ["profileId"] = profileId,
                ["auto"] = acceptSuggestion.ToString()
            }, cancellationToken);

        await _queue.EnqueueAsync(new DocumentWorkItem(document.Id, DocumentProcessingStage.Deduplicate), cancellationToken);
    }

    public Task RequeueAsync(string documentId, DocumentProcessingStage stage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id required", nameof(documentId));
        return _queue.EnqueueAsync(new DocumentWorkItem(documentId, stage), cancellationToken).AsTask();
    }

    private static async Task<SourceDocument?> FindDuplicateAsync(string? hash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        var query = await SourceDocument.Query($"Hash == '{hash}'", cancellationToken);
        return query.FirstOrDefault();
    }

    private static Task RecordEventAsync(SourceDocument document, DocumentProcessingStage stage, DocumentProcessingStatus status, string message, Dictionary<string, string> context, CancellationToken cancellationToken)
    {
        var evt = new DocumentProcessingEvent
        {
            DocumentId = document.Id,
            Stage = stage,
            Status = status,
            Message = message,
            Context = context,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return evt.Save(cancellationToken);
    }
}
