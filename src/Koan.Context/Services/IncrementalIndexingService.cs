using System.Security.Cryptography;
using System.Text;
using Koan.Context.Models;
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
public class IncrementalIndexingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IncrementalIndexingService> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public IncrementalIndexingService(
        IServiceProvider serviceProvider,
        ILogger<IncrementalIndexingService> logger,
        IOptions<FileMonitoringOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(options.Value.MaxConcurrentReindexOperations);
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
            var extraction = scope.ServiceProvider.GetRequiredService<IContentExtractionService>();
            var chunking = scope.ServiceProvider.GetRequiredService<IChunkingService>();
            var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

            var project = await Project.Get(projectId, cancellationToken);
            if (project == null)
            {
                _logger.LogWarning("Project {ProjectId} not found", projectId);
                return;
            }

            // Update project status
            project.Status = IndexingStatus.Updating;
            await Project.UpsertAsync(project, cancellationToken);

            using (EntityContext.Partition($"proj-{projectId:N}"))
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
            await Project.UpsertAsync(project, cancellationToken);

            _logger.LogInformation("Completed incremental indexing for project {Name}", project.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental indexing failed for project {ProjectId}", projectId);

            var project = await Project.Get(projectId, cancellationToken);
            if (project != null)
            {
                project.Status = IndexingStatus.Failed;
                project.IndexingError = ex.Message;
                await Project.UpsertAsync(project, cancellationToken);
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
        IContentExtractionService extraction,
        IChunkingService chunking,
        IEmbeddingService embedding,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, change.Path);

        switch (change.Type)
        {
            case FileChangeType.Deleted:
                await DeleteChunksForFileAsync(relativePath, cancellationToken);
                break;

            case FileChangeType.Modified:
                // Delete old chunks
                await DeleteChunksForFileAsync(relativePath, cancellationToken);

                // Re-index if file still exists
                if (File.Exists(change.Path))
                {
                    await IndexSingleFileAsync(project, change.Path, extraction, chunking, embedding, cancellationToken);
                }
                break;
        }
    }

    private async Task DeleteChunksForFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        // Query all chunks for this file
        var chunks = await DocumentChunk.Query(
            c => c.FilePath == relativePath,
            cancellationToken);

        // Delete from both relational and vector stores
        foreach (var chunk in chunks)
        {
            await chunk.Delete(cancellationToken);

            // Also delete from vector store
            try
            {
                await Vector<DocumentChunk>.Delete(chunk.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete vector for chunk {ChunkId}", chunk.Id);
            }
        }

        _logger.LogDebug("Deleted {Count} chunks for file {Path}", chunks.Count, relativePath);
    }

    private async Task IndexSingleFileAsync(
        Project project,
        string filePath,
        IContentExtractionService extraction,
        IChunkingService chunking,
        IEmbeddingService embedding,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(project.RootPath, filePath);
        var category = PathCategorizer.DeriveCategory(relativePath);
        var pathSegments = PathCategorizer.GetPathSegments(relativePath);

        // Extract content
        var extracted = await extraction.ExtractAsync(filePath, cancellationToken);

        // Get file metadata
        var fileInfo = new FileInfo(filePath);
        var fileHash = await ComputeFileHashAsync(filePath);

        // Chunk content
        await foreach (var chunk in chunking.ChunkAsync(
            extracted,
            project.Id.ToString(),
            commitSha: null,
            cancellationToken))
        {
            // Generate embedding
            var embeddingVector = await embedding.EmbedAsync(chunk.Text, cancellationToken);

            // Create DocumentChunk entity
            var docChunk = DocumentChunk.Create(
                projectId: chunk.ProjectId,
                filePath: relativePath,
                searchText: chunk.Text,
                tokenCount: chunk.TokenCount,
                commitSha: null,
                title: chunk.Title,
                language: chunk.Language);

            docChunk.ChunkRange = $"{chunk.StartOffset}:{chunk.EndOffset}";
            docChunk.Category = category;
            docChunk.PathSegments = pathSegments;
            docChunk.FileLastModified = fileInfo.LastWriteTimeUtc;
            docChunk.FileHash = fileHash;

            // Save to relational store
            await docChunk.Save(cancellationToken);

            // Save to vector store
            await Vector<DocumentChunk>.Save(new[]
            {
                (Id: docChunk.Id,
                 Embedding: embeddingVector,
                 Metadata: (object?)new
                 {
                     docChunk.ProjectId,
                     docChunk.FilePath,
                     docChunk.SearchText,
                     docChunk.Category,
                     docChunk.Language
                 })
            }, cancellationToken);
        }

        _logger.LogDebug("Re-indexed file {Path}", relativePath);
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        return Convert.ToHexString(hashBytes);
    }
}
