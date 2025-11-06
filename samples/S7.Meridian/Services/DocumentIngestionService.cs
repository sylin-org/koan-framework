using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFileCollection files, bool forceReprocess, string? typeHint, CancellationToken ct);
    Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFile file, bool forceReprocess, string? typeHint, CancellationToken ct);
}

public sealed record DocumentIngestionResult(
    IReadOnlyList<SourceDocument> NewDocuments,
    IReadOnlyList<SourceDocument> ReusedDocuments)
{
    public bool HasNewDocuments => NewDocuments.Count > 0;
    public bool HasReusedDocuments => ReusedDocuments.Count > 0;
}

public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDocumentStorage _storage;
    private readonly ISecureUploadValidator _validator;
    private readonly IRunLogWriter _runLog;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(IDocumentStorage storage, ISecureUploadValidator validator, IRunLogWriter runLog, ILogger<DocumentIngestionService> logger)
    {
        _storage = storage;
        _validator = validator;
        _runLog = runLog;
        _logger = logger;
    }

    public async Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFileCollection files, bool forceReprocess, string? typeHint, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("At least one file is required for ingestion.", nameof(files));
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {pipelineId} not found.");

        var existingDocuments = await pipeline.LoadDocumentsAsync(ct);
        var documentsByHash = existingDocuments
            .Where(doc => !string.IsNullOrWhiteSpace(doc.ContentHash))
            .ToDictionary(doc => doc.ContentHash!, doc => doc, StringComparer.OrdinalIgnoreCase);

        var newDocuments = new List<SourceDocument>();
        var reusedDocuments = new List<SourceDocument>();
        var reuseTelemetryCandidates = new List<SourceDocument>();
        var attachmentsAdded = false;
        var storedDocuments = new List<(SourceDocument Doc, string StorageKey)>(); // Track for rollback

        try
        {
            foreach (var file in files)
        {
            if (file is null || file.Length == 0)
            {
                _logger.LogWarning("Skipping empty upload for pipeline {PipelineId}.", pipeline.Id);
                continue;
            }

            _logger.LogInformation("Processing file upload: {FileName}, Size: {Size} bytes", file.FileName, file.Length);

            await _validator.ValidateAsync(file, ct);

            var buffered = await BufferAndHashAsync(file, ct);
            await using var contentStream = buffered.Content;
            var hash = buffered.Hash;

            _logger.LogInformation("File {FileName} hashed to {Hash}", file.FileName, hash);

            // Try to find existing document by content hash (no filename-based best guess)
            documentsByHash.TryGetValue(hash, out var existingByHash);

            if (existingByHash is not null)
            {
                var before = pipeline.DocumentIds.Count;
                var wasAlreadyAttached = pipeline.DocumentIds.Contains(existingByHash.Id!);

                pipeline.AttachDocument(existingByHash.Id!);
                var attached = pipeline.DocumentIds.Count > before;

                _logger.LogInformation(
                    "Document {DocumentId} reuse: wasAlreadyAttached={WasAlreadyAttached}, attached={Attached}, before={Before}, after={After}",
                    existingByHash.Id, wasAlreadyAttached, attached, before, pipeline.DocumentIds.Count);

                if (attached)
                {
                    attachmentsAdded = true;
                    reuseTelemetryCandidates.Add(existingByHash);
                }

                var requiresReprocess = forceReprocess || !string.IsNullOrWhiteSpace(typeHint);

                // Check if extraction schema has changed since last extraction, or if never extracted
                if (!requiresReprocess && !string.IsNullOrWhiteSpace(pipeline.AnalysisTypeId))
                {
                    var analysisType = await AnalysisType.Get(pipeline.AnalysisTypeId!, ct);
                    if (analysisType != null)
                    {
                        // Reprocess if never extracted OR if analysis type version has changed
                        if (!existingByHash.LastExtractedAnalysisTypeVersion.HasValue)
                        {
                            requiresReprocess = true;
                            _logger.LogInformation(
                                "Document {DocumentId} requires processing - never extracted for this analysis type",
                                existingByHash.Id);
                        }
                        else if (analysisType.Version > existingByHash.LastExtractedAnalysisTypeVersion.Value)
                        {
                            requiresReprocess = true;
                            _logger.LogInformation(
                                "Document {DocumentId} requires reprocessing - AnalysisType version changed from {OldVersion} to {NewVersion}",
                                existingByHash.Id, existingByHash.LastExtractedAnalysisTypeVersion.Value, analysisType.Version);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(typeHint))
                {
                    ApplyManualClassification(existingByHash, typeHint, "Upload hint");
                    existingByHash.Status = DocumentProcessingStatus.Pending;
                    existingByHash.UpdatedAt = DateTime.UtcNow;
                    var updated = await existingByHash.Save(ct);
                    documentsByHash[hash] = updated;
                    ReplaceDocument(existingDocuments, updated);
                    existingByHash = updated;

                    await AppendClassificationAuditAsync(pipeline, existingByHash, "manual-hint", ct);
                }

                if (requiresReprocess)
                {
                    existingByHash.Status = DocumentProcessingStatus.Pending;
                    existingByHash.UpdatedAt = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(existingByHash.ContentHash))
                    {
                        existingByHash.ContentHash = hash;
                    }

                    var refreshed = await existingByHash.Save(ct);
                    documentsByHash[hash] = refreshed;
                    ReplaceDocument(existingDocuments, refreshed);
                    newDocuments.Add(refreshed);
                    attachmentsAdded = true;
                    _logger.LogInformation("Queued existing document {DocumentId} for reprocessing in pipeline {PipelineId}.", refreshed.Id, pipeline.Id);
                }
                else
                {
                    reusedDocuments.Add(existingByHash);
                    _logger.LogInformation("Reused existing document {DocumentId} for pipeline {PipelineId} (hash match).", existingByHash.Id, pipeline.Id);
                }

                continue;
            }

            contentStream.Position = 0;
            var storageKey = await _storage.StoreAsync(contentStream, file.FileName, file.ContentType, ct);

            var document = new SourceDocument
            {
                OriginalFileName = file.FileName,
                StorageKey = storageKey,
                SourceType = MeridianConstants.SourceTypes.Unspecified,
                MediaType = file.ContentType,
                Size = file.Length,
                ContentHash = hash,
                Status = DocumentProcessingStatus.Pending,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(typeHint))
            {
                ApplyManualClassification(document, typeHint, "Upload hint");
            }

            var saved = await document.Save(ct);

            // Track for rollback in case pipeline.Save fails
            storedDocuments.Add((saved, storageKey));

            pipeline.AttachDocument(saved.Id!);
            attachmentsAdded = true;
            documentsByHash[hash] = saved;
            existingDocuments.Add(saved);
            newDocuments.Add(saved);

            _logger.LogInformation("Stored document {DocumentId} for pipeline {PipelineId}.", saved.Id, pipeline.Id);

            if (!string.IsNullOrWhiteSpace(typeHint))
            {
                await AppendClassificationAuditAsync(pipeline, saved, "manual-hint", ct);
            }
            }

        if (newDocuments.Count == 0 && reusedDocuments.Count == 0)
        {
            throw new InvalidOperationException("No valid files were provided for ingestion.");
        }

            if (attachmentsAdded)
            {
                pipeline.UpdatedAt = DateTime.UtcNow;
                await pipeline.Save(ct);
                _logger.LogInformation("Pipeline {PipelineId} now has {Count} attached documents: {DocumentIds}",
                    pipeline.Id, pipeline.DocumentIds.Count, string.Join(", ", pipeline.DocumentIds));
            }

            if (reuseTelemetryCandidates.Count > 0)
            {
                await EmitDocumentReuseTelemetryAsync(pipeline, reuseTelemetryCandidates, ct);
            }

            _logger.LogInformation(
                "Upload complete for pipeline {PipelineId}: {NewCount} new documents, {ReusedCount} reused documents. New IDs: [{NewIds}], Reused IDs: [{ReusedIds}]",
                pipeline.Id,
                newDocuments.Count,
                reusedDocuments.Count,
                string.Join(", ", newDocuments.Select(d => d.Id)),
                string.Join(", ", reusedDocuments.Select(d => d.Id)));

            return new DocumentIngestionResult(newDocuments, reusedDocuments);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Rollback: Delete any storage objects and entities we created
            _logger.LogError(ex, "Upload failed for pipeline {PipelineId}, rolling back {Count} stored documents",
                pipeline.Id, storedDocuments.Count);

            foreach (var (doc, storageKey) in storedDocuments)
            {
                try
                {
                    // Delete from blob storage
                    await _storage.DeleteAsync(storageKey, ct);
                    _logger.LogDebug("Rolled back storage for document {DocumentId}, key {StorageKey}",
                        doc.Id, storageKey);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed for storage key {Key}, document {DocumentId}",
                        storageKey, doc.Id);
                }

                // Note: We don't delete SourceDocument entities as they may have been indexed already
                // and deleting them could cause referential integrity issues. Instead, they'll be
                // orphaned but can be cleaned up by a background job later.
            }

            throw; // Re-throw to inform caller
        }
    }

    public async Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFile file, bool forceReprocess, string? typeHint, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return await IngestAsync(pipelineId, new FormFileCollection { file }, forceReprocess, typeHint, ct);
    }

    private static void ApplyManualClassification(SourceDocument document, string typeId, string reason)
    {
        document.SourceType = typeId;
        document.ClassifiedTypeId = typeId;
        document.ClassifiedTypeVersion = 1;
        document.ClassificationConfidence = 1.0;
        document.ClassificationMethod = ClassificationMethod.Manual;
        document.ClassificationReason = reason;
    }

    private async Task AppendClassificationAuditAsync(DocumentPipeline pipeline, SourceDocument document, string mode, CancellationToken ct)
    {
        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id ?? string.Empty,
            Stage = "classify",
            DocumentId = document.Id,
            Status = mode,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["typeId"] = document.ClassifiedTypeId ?? document.SourceType,
                ["method"] = document.ClassificationMethod.ToString(),
                ["reason"] = document.ClassificationReason ?? string.Empty,
                ["confidence"] = document.ClassificationConfidence.ToString("0.00", CultureInfo.InvariantCulture)
            }
        }, ct);
    }

    private static async Task<(string Hash, MemoryStream Content)> BufferAndHashAsync(IFormFile file, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        var capacity = file.Length is > 0 and < int.MaxValue ? (int)file.Length : 0;
        var memory = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();

        await using var source = file.OpenReadStream();
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await memory.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        memory.Position = 0;
        var hashBytes = hasher.GetHashAndReset();
        var hash = Convert.ToHexString(hashBytes);
        memory.Position = 0;
        return (hash, memory);
    }

    private static void ReplaceDocument(List<SourceDocument> documents, SourceDocument updated)
    {
        var index = documents.FindIndex(doc => string.Equals(doc.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            documents[index] = updated;
        }
    }

    private async Task EmitDocumentReuseTelemetryAsync(DocumentPipeline pipeline, IReadOnlyList<SourceDocument> reusedDocuments, CancellationToken ct)
    {
        var distinctDocuments = reusedDocuments
            .Where(doc => !string.IsNullOrWhiteSpace(doc.Id))
            .GroupBy(doc => doc.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        foreach (var document in distinctDocuments)
        {
            var pipelinesUsingDocument = await DocumentPipeline.Query(p => p.DocumentIds.Contains(document.Id!), ct);
            var pipelineIds = pipelinesUsingDocument
                .Select(p => p.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pipelineIds.Count <= 1)
            {
                continue;
            }

            var sharedWith = pipelineIds
                .Where(id => !string.Equals(id, pipeline.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pipelineCount"] = pipelineIds.Count.ToString(CultureInfo.InvariantCulture),
                ["ingestingPipeline"] = pipeline.Id ?? string.Empty
            };

            if (sharedWith.Count > 0)
            {
                metadata["sharedWith"] = string.Join(',', sharedWith);
            }

            if (!string.IsNullOrWhiteSpace(document.ContentHash))
            {
                metadata["contentHash"] = document.ContentHash!;
            }

            var timestamp = DateTime.UtcNow;

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id ?? string.Empty,
                Stage = "document-reuse",
                DocumentId = document.Id,
                StartedAt = timestamp,
                FinishedAt = timestamp,
                Status = "shared",
                Metadata = metadata
            }, ct);
        }
    }
}
