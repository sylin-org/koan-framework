using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFileCollection files, bool forceReprocess, CancellationToken ct);
    Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFile file, bool forceReprocess, CancellationToken ct);
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
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(IDocumentStorage storage, ISecureUploadValidator validator, ILogger<DocumentIngestionService> logger)
    {
        _storage = storage;
        _validator = validator;
        _logger = logger;
    }

    public async Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFileCollection files, bool forceReprocess, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("At least one file is required for ingestion.", nameof(files));
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {pipelineId} not found.");

        var existingDocuments = await pipeline.LoadDocumentsAsync(ct).ConfigureAwait(false);
        var documentsByHash = existingDocuments
            .Where(doc => !string.IsNullOrWhiteSpace(doc.ContentHash))
            .ToDictionary(doc => doc.ContentHash!, doc => doc, StringComparer.OrdinalIgnoreCase);

        var newDocuments = new List<SourceDocument>();
        var reusedDocuments = new List<SourceDocument>();
        var attachmentsAdded = false;

        foreach (var file in files)
        {
            if (file is null || file.Length == 0)
            {
                _logger.LogWarning("Skipping empty upload for pipeline {PipelineId}.", pipeline.Id);
                continue;
            }

            await _validator.ValidateAsync(file, ct).ConfigureAwait(false);

            var buffered = await BufferAndHashAsync(file, ct).ConfigureAwait(false);
            await using var contentStream = buffered.Content;
            var hash = buffered.Hash;

            if (!documentsByHash.TryGetValue(hash, out var existingByHash))
            {
                existingByHash = FindByBestGuess(existingDocuments, file);
                if (existingByHash is not null)
                {
                    existingByHash.ContentHash = hash;
                    existingByHash.UpdatedAt = DateTime.UtcNow;
                    var normalized = await existingByHash.Save(ct).ConfigureAwait(false);
                    documentsByHash[hash] = normalized;
                    ReplaceDocument(existingDocuments, normalized);
                    existingByHash = normalized;
                }
            }

            if (existingByHash is not null)
            {
                var before = pipeline.DocumentIds.Count;
                pipeline.AttachDocument(existingByHash.Id!);
                if (pipeline.DocumentIds.Count > before)
                {
                    attachmentsAdded = true;
                }

                if (forceReprocess)
                {
                    existingByHash.Status = DocumentProcessingStatus.Pending;
                    existingByHash.UpdatedAt = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(existingByHash.ContentHash))
                    {
                        existingByHash.ContentHash = hash;
                    }

                    var refreshed = await existingByHash.Save(ct).ConfigureAwait(false);
                    documentsByHash[hash] = refreshed;
                    ReplaceDocument(existingDocuments, refreshed);
                    newDocuments.Add(refreshed);
                    attachmentsAdded = true;
                    _logger.LogInformation("Force reprocess enabled; queued existing document {DocumentId} for pipeline {PipelineId}.", refreshed.Id, pipeline.Id);
                }
                else
                {
                    reusedDocuments.Add(existingByHash);
                    _logger.LogInformation("Reused existing document {DocumentId} for pipeline {PipelineId} (hash match).", existingByHash.Id, pipeline.Id);
                }

                continue;
            }

            contentStream.Position = 0;
            var storageKey = await _storage.StoreAsync(contentStream, file.FileName, file.ContentType, ct).ConfigureAwait(false);

            var document = new SourceDocument
            {
                OriginalFileName = file.FileName,
                StorageKey = storageKey,
                SourceType = MeridianConstants.SourceTypes.Unclassified,
                MediaType = file.ContentType,
                Size = file.Length,
                ContentHash = hash,
                Status = DocumentProcessingStatus.Pending,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var saved = await document.Save(ct).ConfigureAwait(false);
            pipeline.AttachDocument(saved.Id!);
            attachmentsAdded = true;
            documentsByHash[hash] = saved;
            existingDocuments.Add(saved);
            newDocuments.Add(saved);

            _logger.LogInformation("Stored document {DocumentId} for pipeline {PipelineId}.", saved.Id, pipeline.Id);
        }

        if (newDocuments.Count == 0 && reusedDocuments.Count == 0)
        {
            throw new InvalidOperationException("No valid files were provided for ingestion.");
        }

        if (attachmentsAdded)
        {
            pipeline.UpdatedAt = DateTime.UtcNow;
            await pipeline.Save(ct).ConfigureAwait(false);
        }

        return new DocumentIngestionResult(newDocuments, reusedDocuments);
    }

    public async Task<DocumentIngestionResult> IngestAsync(string pipelineId, IFormFile file, bool forceReprocess, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return await IngestAsync(pipelineId, new FormFileCollection { file }, forceReprocess, ct).ConfigureAwait(false);
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
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await memory.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
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

    private static SourceDocument? FindByBestGuess(IReadOnlyList<SourceDocument> existingDocuments, IFormFile file)
    {
        return existingDocuments.FirstOrDefault(doc =>
            string.IsNullOrWhiteSpace(doc.ContentHash) &&
            doc.Size == file.Length &&
            string.Equals(doc.OriginalFileName, file.FileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceDocument(List<SourceDocument> documents, SourceDocument updated)
    {
        var index = documents.FindIndex(doc => string.Equals(doc.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            documents[index] = updated;
        }
    }
}
