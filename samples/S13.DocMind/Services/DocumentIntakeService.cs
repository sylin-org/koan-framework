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
using S13.DocMind.Infrastructure.Repositories;
using S13.DocMind.Models;
using Koan.Data.Core;

namespace S13.DocMind.Services;

public sealed class DocumentIntakeService : IDocumentIntakeService
{
    private readonly IDocumentStorage _storage;
    private readonly DocMindOptions _options;
    private readonly ILogger<DocumentIntakeService> _logger;
    private readonly TimeProvider _clock;
    private readonly IDocumentProcessingEventSink _eventSink;

    public DocumentIntakeService(
        IDocumentStorage storage,
        IOptions<DocMindOptions> options,
        ILogger<DocumentIntakeService> logger,
        TimeProvider clock,
        IDocumentProcessingEventSink eventSink)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
        _eventSink = eventSink;
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
            _logger.LogWarning(
                "File {File} uploaded with unsupported content type {ContentType}",
                request.File.FileName,
                request.File.ContentType);
        }

        await using var stream = request.File.OpenReadStream();
        var stored = await _storage.SaveAsync(request.File.FileName, stream, cancellationToken);

        var duplicate = await SourceDocumentRepository.FindByHashAsync(stored.Hash ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await _storage.TryDeleteAsync(stored.ToLocation(), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Detected duplicate upload {File} -> {DocumentId}",
                request.File.FileName,
                duplicate.Id);

            return new DocumentUploadReceipt
            {
                DocumentId = duplicate.Id,
                FileName = duplicate.DisplayName ?? duplicate.FileName,
                Status = duplicate.Status,
                Duplicate = true,
                Sha512 = duplicate.Sha512,
                Tags = new Dictionary<string, string>(duplicate.Tags, StringComparer.OrdinalIgnoreCase)
            };
        }

        var now = _clock.GetUtcNow();
        var document = new SourceDocument
        {
            FileName = Path.GetFileName(stored.ObjectKey),
            DisplayName = request.File.FileName,
            ContentType = request.File.ContentType,
            FileSizeBytes = stored.Length,
            Sha512 = stored.Hash ?? string.Empty,
            Storage = stored.ToLocation(),
            UploadedAt = now,
            Description = request.Description,
            Summary =
            {
                LastCompletedStage = DocumentProcessingStage.Upload,
                LastKnownStatus = DocumentProcessingStatus.Uploaded,
                LastStageCompletedAt = now
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

        await document.Save(cancellationToken).ConfigureAwait(false);

        var documentId = Guid.Parse(document.Id);

        await _eventSink.RecordAsync(
            new DocumentProcessingEventEntry(
                documentId,
                DocumentProcessingStage.Upload,
                DocumentProcessingStatus.Uploaded,
                Detail: "Document uploaded",
                Context: new Dictionary<string, string>
                {
                    ["fileName"] = document.DisplayName ?? document.FileName,
                    ["contentType"] = document.ContentType,
                    ["length"] = stored.Length.ToString(CultureInfo.InvariantCulture)
                }),
            cancellationToken).ConfigureAwait(false);

        document.Status = DocumentProcessingStatus.Queued;
        await document.Save(cancellationToken).ConfigureAwait(false);

        await EnsureJobQueuedAsync(documentId, DocumentProcessingStage.ExtractText, cancellationToken).ConfigureAwait(false);

        return new DocumentUploadReceipt
        {
            DocumentId = document.Id,
            FileName = document.DisplayName ?? document.FileName,
            Status = document.Status,
            Duplicate = false,
            Sha512 = document.Sha512,
            Tags = new Dictionary<string, string>(document.Tags, StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task AssignProfileAsync(SourceDocument document, string profileId, bool acceptSuggestion, CancellationToken cancellationToken)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Profile id required", nameof(profileId));

        document.AssignedProfileId = profileId;
        document.AssignedBySystem = acceptSuggestion;
        document.LastProcessedAt = _clock.GetUtcNow();
        await document.Save(cancellationToken).ConfigureAwait(false);

        var documentId = Guid.Parse(document.Id);

        await _eventSink.RecordAsync(
            new DocumentProcessingEventEntry(
                documentId,
                DocumentProcessingStage.Aggregate,
                DocumentProcessingStatus.Queued,
                Detail: acceptSuggestion ? "Profile auto-accepted" : "Profile assignment queued",
                Context: new Dictionary<string, string>
                {
                    ["profileId"] = profileId,
                    ["auto"] = acceptSuggestion.ToString()
                }),
            cancellationToken).ConfigureAwait(false);

        await EnsureJobQueuedAsync(documentId, DocumentProcessingStage.Aggregate, cancellationToken).ConfigureAwait(false);
    }

    public Task RequeueAsync(string documentId, DocumentProcessingStage stage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id required", nameof(documentId));
        if (!Guid.TryParse(documentId, out var id))
        {
            throw new ValidationException("Document id must be a GUID");
        }

        return EnsureJobQueuedAsync(id, stage, cancellationToken);
    }

    private async Task EnsureJobQueuedAsync(Guid documentId, DocumentProcessingStage stage, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var job = await DocumentProcessingJobRepository.FindByDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            job = new DocumentProcessingJob
            {
                SourceDocumentId = documentId,
                Stage = stage,
                Status = DocumentProcessingStatus.Queued,
                CreatedAt = now,
                UpdatedAt = now,
                NextAttemptAt = now,
                MaxAttempts = _options.Processing.MaxRetryAttempts
            };

            job.MarkStageQueued(stage, now, job.CorrelationId);

            await job.Save(cancellationToken).ConfigureAwait(false);
            return;
        }

        job.Stage = stage;
        job.Status = DocumentProcessingStatus.Queued;
        job.Attempt = 0;
        job.RetryCount = 0;
        job.NextAttemptAt = now;
        job.UpdatedAt = now;
        job.LastError = null;
        job.MaxAttempts = _options.Processing.MaxRetryAttempts;
        if (string.IsNullOrWhiteSpace(job.CorrelationId))
        {
            job.CorrelationId = Guid.NewGuid().ToString("N");
        }

        job.MarkStageQueued(stage, now, job.CorrelationId);

        await job.Save(cancellationToken).ConfigureAwait(false);
    }
}
