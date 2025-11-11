using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services.Maintenance;

/// <summary>
/// Provides reusable helpers for deleting chunk metadata and associated vector entries within a project partition.
/// </summary>
public sealed class ChunkMaintenanceService
{
    private readonly ILogger<ChunkMaintenanceService> _logger;

    public ChunkMaintenanceService(ILogger<ChunkMaintenanceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Removes all chunk records (and optionally the IndexedFile manifest entry) for a single file.
    /// </summary>
    /// <param name="relativePath">File path relative to the project root.</param>
    /// <param name="deleteIndexedFile">When true, also delete the IndexedFile manifest entry.</param>
    /// <param name="deleteVectors">When true, remove vector entries for each chunk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChunkMaintenanceResult> RemoveFileAsync(
        string relativePath,
        bool deleteIndexedFile,
        bool deleteVectors = true,
        CancellationToken cancellationToken = default)
    {
        EnsurePartition();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path cannot be null or whitespace", nameof(relativePath));
        }

        var vectorFailures = new List<string>();
        var chunkIds = new List<string>();
    var snapshotsRemoved = 0;

        var indexedFile = await GetIndexedFileAsync(relativePath, cancellationToken);
        var chunks = await Chunk.Query(c => c.IndexedFileId == indexedFile.Id, cancellationToken);

        foreach (var chunk in chunks)
        {
            snapshotsRemoved += await RemoveVectorSnapshotAsync(chunk.Id, cancellationToken);

            if (deleteVectors)
            {
                try
                {
                    await Vector<Chunk>.Delete(chunk.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    vectorFailures.Add(chunk.Id);
                    _logger.LogWarning(ex, "Failed to delete vector for chunk {ChunkId}", chunk.Id);
                }
            }

            await chunk.Delete(cancellationToken);
            chunkIds.Add(chunk.Id);
        }

    if (snapshotsRemoved > 0)
        {
            _logger.LogDebug(
        "Removed {Count} vector snapshots for file {RelativePath}",
        snapshotsRemoved,
                relativePath);
        }

        var manifestRemoved = false;
        if (deleteIndexedFile)
        {
            await indexedFile.Delete(cancellationToken);
            manifestRemoved = true;
        }

        return new ChunkMaintenanceResult(
            relativePath,
            chunkIds.Count,
            deleteVectors ? chunkIds.Count - vectorFailures.Count : 0,
            vectorFailures,
            manifestRemoved);
    }

    /// <summary>
    /// Removes every chunk record in the current partition.
    /// </summary>
    /// <param name="deleteIndexedFiles">When true, also delete indexed file manifest entries.</param>
    /// <param name="deleteVectors">When true, remove vector entries for each chunk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChunkBulkMaintenanceResult> RemoveAllChunksAsync(
        bool deleteIndexedFiles,
        bool deleteVectors = true,
        CancellationToken cancellationToken = default)
    {
        EnsurePartition();

        var chunkCount = 0;
        var vectorSuccess = 0;
    var vectorFailures = new List<string>();
    var snapshotsRemoved = 0;

        await foreach (var chunk in Chunk.AllStream(ct: cancellationToken))
        {
            chunkCount++;

            snapshotsRemoved += await RemoveVectorSnapshotAsync(chunk.Id, cancellationToken);

            if (deleteVectors)
            {
                try
                {
                    await Vector<Chunk>.Delete(chunk.Id, cancellationToken);
                    vectorSuccess++;
                }
                catch (Exception ex)
                {
                    vectorFailures.Add(chunk.Id);
                    _logger.LogWarning(ex, "Failed to delete vector for chunk {ChunkId}", chunk.Id);
                }
            }

            await chunk.Delete(cancellationToken);
        }

    if (snapshotsRemoved > 0)
        {
            _logger.LogDebug(
        "Removed {Count} vector snapshots while clearing all chunks",
        snapshotsRemoved);
        }

        var manifestDeleted = 0;
        if (deleteIndexedFiles)
        {
            await foreach (var file in IndexedFile.AllStream(ct: cancellationToken))
            {
                await file.Delete(cancellationToken);
                manifestDeleted++;
            }
        }

        return new ChunkBulkMaintenanceResult(
            chunkCount,
            deleteVectors ? vectorSuccess : 0,
            vectorFailures,
            manifestDeleted);
    }

    private static async Task<IndexedFile> GetIndexedFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var indexedFiles = await IndexedFile.Query(f => f.RelativePath == relativePath, cancellationToken);
        var indexedFile = indexedFiles.FirstOrDefault();
        if (indexedFile is null)
        {
            throw new InvalidOperationException($"No indexed file manifest entry found for {relativePath}");
        }

        return indexedFile;
    }

    private static async Task<int> RemoveVectorSnapshotAsync(string chunkId, CancellationToken cancellationToken)
    {
        using (EntityContext.With(partition: null))
        {
            var snapshot = await ChunkVectorState.Get(chunkId, cancellationToken);
            if (snapshot is null)
            {
                return 0;
            }

            await snapshot.Remove(cancellationToken);
            return 1;
        }
    }

    /// <summary>
    /// Removes the supplied set of file paths by delegating to <see cref="RemoveFileAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<ChunkMaintenanceResult>> RemoveFilesAsync(
        IEnumerable<string> relativePaths,
        bool deleteIndexedFile,
        bool deleteVectors = true,
        CancellationToken cancellationToken = default)
    {
        EnsurePartition();

        var results = new List<ChunkMaintenanceResult>();
        foreach (var path in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await RemoveFileAsync(path, deleteIndexedFile, deleteVectors, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private static void EnsurePartition()
    {
        if (EntityContext.Current?.Partition is null)
        {
            throw new InvalidOperationException("Chunk maintenance requires an active project partition. Call EntityContext.Partition(projectId) first.");
        }
    }
}

/// <summary>
/// Result for a per-file chunk maintenance operation.
/// </summary>
public sealed record ChunkMaintenanceResult(
    string RelativePath,
    int ChunksDeleted,
    int VectorsDeleted,
    IReadOnlyList<string> VectorDeleteFailures,
    bool IndexedFileRemoved);

/// <summary>
/// Result for a bulk chunk maintenance operation.
/// </summary>
public sealed record ChunkBulkMaintenanceResult(
    int ChunksDeleted,
    int VectorsDeleted,
    IReadOnlyList<string> VectorDeleteFailures,
    int IndexedFilesDeleted);
