using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Context.Services.Maintenance;
using Koan.Context.Utilities;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Context.Services;

/// <summary>
/// Handles incremental re-indexing of changed files
/// </summary>
public class IncrementalIndexer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IncrementalIndexer> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ChunkMaintenanceService _chunkMaintenance;
    private readonly TagResolver _tagResolver;

    public IncrementalIndexer(
        IServiceProvider serviceProvider,
        ILogger<IncrementalIndexer> logger,
        IOptions<FileMonitoringOptions> options,
        ChunkMaintenanceService chunkMaintenance,
        TagResolver tagResolver)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(options.Value.MaxConcurrentReindexOperations);
        _chunkMaintenance = chunkMaintenance ?? throw new ArgumentNullException(nameof(chunkMaintenance));
        _tagResolver = tagResolver ?? throw new ArgumentNullException(nameof(tagResolver));
    }

    public async Task ProcessFileChangesAsync(
        string projectId,
        List<FileChange> changes,
        CancellationToken cancellationToken = default)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var extraction = scope.ServiceProvider.GetRequiredService<Extraction>();
            var chunking = scope.ServiceProvider.GetRequiredService<Chunker>();
            var embedding = scope.ServiceProvider.GetRequiredService<Embedding>();

            var project = await Project.Get(projectId, cancellationToken);
            if (project == null)
            {
                _logger.LogWarning("Project {ProjectId} not found", projectId);
                return;
            }

            // Update project status
            project.Status = IndexingStatus.Indexing;
            await project.Save(cancellationToken);

            using (EntityContext.Partition(projectId))
            {
                foreach (var change in changes)
                {
                    try
                    {
                        await ProcessSingleFileChangeAsync(project, change, extraction, chunking, embedding, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process file change: {Path}", change.Path);
                    }
                }
            }

            // Update project metadata
            project.LastIndexed = DateTime.UtcNow;
            project.Status = IndexingStatus.Ready;
            await project.Save(cancellationToken);

            _logger.LogInformation("Completed incremental indexing for project {Name}", project.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental indexing failed for project {ProjectId}", projectId);

            var project = await Project.Get(projectId, cancellationToken);
            if (project != null)
            {
                project.Status = IndexingStatus.Failed;
                project.LastError = ex.Message;
                await project.Save(cancellationToken);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task ProcessSingleFileChangeAsync(
        Project project,
        FileChange change,
        Extraction extraction,
        Chunker chunking,
        Embedding embedding,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, change.Path);

        switch (change.Type)
        {
            case FileChangeType.Deleted:
                await _chunkMaintenance.RemoveFileAsync(
                    relativePath,
                    deleteIndexedFile: true,
                    deleteVectors: true,
                    cancellationToken: cancellationToken);
                break;

            case FileChangeType.Modified:
                // Delete old chunks
                await _chunkMaintenance.RemoveFileAsync(
                    relativePath,
                    deleteIndexedFile: false,
                    deleteVectors: true,
                    cancellationToken: cancellationToken);

                // Re-index if file still exists
                if (File.Exists(change.Path))
                {
                    await IndexSingleFileAsync(project, change.Path, extraction, chunking, embedding, cancellationToken);
                }
                break;
        }
    }

    private async Task IndexSingleFileAsync(
        Project project,
        string filePath,
        Extraction extraction,
        Chunker chunking,
        Embedding embedding,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, filePath);
        var fileInfo = new FileInfo(filePath);
        var fileHash = await FileHasher.ComputeSha256Async(filePath, cancellationToken);

        var indexedFileResults = await IndexedFile.Query(
            f => f.RelativePath == relativePath,
            cancellationToken);
        var indexedFile = indexedFileResults.FirstOrDefault();

        if (indexedFile == null)
        {
            indexedFile = IndexedFile.Create(
                relativePath,
                fileHash,
                fileInfo.Length);
        }
        else
        {
            indexedFile.UpdateAfterIndexing(fileHash, fileInfo.Length);
        }

        // 2. Extract content and derive metadata
        var extracted = await extraction.ExtractAsync(
            filePath,
            relativePath,
            cancellationToken);
        var frontmatter = FrontmatterParser.Parse(extracted.FullText);
        var pathSegments = PathMetadata.GetPathSegments(relativePath);

        var fileTagInput = TagResolverInput.ForFile(
            project.Id,
            relativePath,
            pipelineName: null,
            language: null,
            frontmatter: frontmatter.Metadata,
            fileTags: frontmatter.Tags);
        var fileTagResult = await _tagResolver.ResolveAsync(fileTagInput, cancellationToken);
        indexedFile.SetTagEnvelope(fileTagResult.Envelope);
        await indexedFile.Save(cancellationToken);

        var inheritedTags = GetInheritedTags(fileTagResult.Envelope);

        // Chunk content
        await foreach (var chunk in chunking.ChunkAsync(
            extracted,
            project.Id.ToString(),
            commitSha: null,
            cancellationToken))
        {
            var provenance = ComputeProvenance(extracted.FullText, chunk.StartOffset, chunk.EndOffset);

            // Generate embedding
            var embeddingVector = await embedding.EmbedAsync(chunk.Text, cancellationToken);

            // Create Chunk entity (within partition context, linked to IndexedFile)
            var docChunk = Chunk.Create(
                indexedFileId: indexedFile.Id,
                filePath: chunk.FilePath,
                searchText: chunk.Text,
                tokenCount: chunk.TokenCount,
                commitSha: null,
                title: chunk.Title,
                language: chunk.Language);

            docChunk.StartByteOffset = provenance.StartByteOffset;
            docChunk.EndByteOffset = provenance.EndByteOffset;
            docChunk.StartLine = provenance.StartLine;
            docChunk.EndLine = provenance.EndLine;
            docChunk.PathSegments = pathSegments;
            docChunk.FileLastModified = fileInfo.LastWriteTimeUtc;
            docChunk.FileHash = fileHash;

            var chunkTagInput = fileTagInput.ForChunk(chunk.Language, chunk.Text, inheritedTags);
            var chunkTagResult = await _tagResolver.ResolveAsync(chunkTagInput, cancellationToken);
            docChunk.SetTagEnvelope(chunkTagResult.Envelope);

            // Save to relational store
            await docChunk.Save(cancellationToken);

            // Save to vector store
            await Vector<Chunk>.Save(new[]
            {
                (Id: docChunk.Id,
                 Embedding: embeddingVector,
                 Metadata: (object?)new ChunkVectorMetadata
                 {
                     FilePath = docChunk.FilePath,
                     SearchText = docChunk.SearchText,
                     CommitSha = docChunk.CommitSha,
                     StartByteOffset = docChunk.StartByteOffset,
                     EndByteOffset = docChunk.EndByteOffset,
                     StartLine = docChunk.StartLine,
                     EndLine = docChunk.EndLine,
                     SourceUrl = docChunk.SourceUrl,
                     Title = docChunk.Title,
                     Language = docChunk.Language,
                     FileHash = docChunk.FileHash,
                     FileLastModified = docChunk.FileLastModified,
                     PathSegments = docChunk.PathSegments ?? Array.Empty<string>(),
                     PrimaryTags = docChunk.Tags.Primary,
                     SecondaryTags = docChunk.Tags.Secondary,
                     FileTags = docChunk.Tags.File
                 })
            }, cancellationToken);
        }

        _logger.LogDebug("Re-indexed file {Path}", relativePath);
    }

    private static IReadOnlyList<string> GetInheritedTags(TagEnvelope envelope)
    {
        return TagEnvelope.NormalizeTags(
            envelope.Primary
                .Concat(envelope.Secondary)
                .Concat(envelope.File));
    }

    private static (long StartByteOffset, long EndByteOffset, int StartLine, int EndLine) ComputeProvenance(
        string fullText,
        int startOffset,
        int endOffset)
    {
        if (string.IsNullOrEmpty(fullText))
        {
            return (0L, 0L, 1, 1);
        }

        var safeStart = Math.Clamp(startOffset, 0, fullText.Length);
        var safeEnd = Math.Clamp(endOffset, safeStart, fullText.Length);

        var span = fullText.AsSpan();
        var prefixSpan = span[..safeStart];
        var chunkSpan = span[safeStart..safeEnd];

        var encoding = Encoding.UTF8;
        var startBytes = encoding.GetByteCount(prefixSpan);
        var chunkBytes = encoding.GetByteCount(chunkSpan);
        var endBytes = startBytes + chunkBytes;

        var startLine = 1 + CountNewLines(prefixSpan);
        var endLine = chunkSpan.IsEmpty
            ? startLine
            : startLine + CountNewLines(chunkSpan);

        return (startBytes, endBytes, startLine, endLine);
    }

    private static int CountNewLines(ReadOnlySpan<char> span)
    {
        var count = 0;

        foreach (var ch in span)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
