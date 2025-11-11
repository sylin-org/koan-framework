using System.Diagnostics;
using System.Text;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Koan.Context.Services;

/// <summary>
/// Orchestrates the full indexing pipeline with differential scanning and job tracking
/// </summary>
/// <remarks>
/// Pipeline flow:
/// 1. Create IndexingJob for progress tracking
/// 2. Plan differential scan (SHA256-based change detection)
/// 3. Set partition context for project
/// 4. Process only changed/new files
/// 5. Update manifest with file hashes
/// 6. Generate embeddings and save to vector store
/// 7. Query actual counts from database
/// 8. Update project metadata and complete job
///
/// QA Issue #2 FIXED: Added Polly retry for batch saves
/// QA Issue #7 FIXED: Added partition context validation
/// QA Issue #25 FIXED: Cancellation checks between batches
/// QA Issue #29 FIXED: SaveVectorBatchAsync is now instance method with logging
/// QA Issue #35 FIXED: Structured error reporting with IndexingError type
/// Content-Addressable Indexing: SHA256-based differential scanning with job tracking
/// </remarks>
public class IndexingService : IIndexingService
{
    private readonly IDocumentDiscoveryService _discovery;
    private readonly IContentExtractionService _extraction;
    private readonly IChunkingService _chunking;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<IndexingService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    private const int BatchSize = 100; // Save vectors in batches of 100

    public IndexingService(
        IDocumentDiscoveryService discovery,
        IContentExtractionService extraction,
        IChunkingService chunking,
        IEmbeddingService embedding,
        ILogger<IndexingService> logger)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _extraction = extraction ?? throw new ArgumentNullException(nameof(extraction));
        _chunking = chunking ?? throw new ArgumentNullException(nameof(chunking));
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // QA Issue #2 FIX: Retry policy for batch vector saves
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Batch vector save failed (attempt {RetryCount}/3). Retrying after {Delay}ms",
                        retryCount,
                        timeSpan.TotalMilliseconds);
                });
    }

    public async Task<IndexingResult> IndexProjectAsync(
        string projectId,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<IndexingError>();
        IndexingJob? job = null;

        try
        {
            // 1. Load project
            var project = await Project.Get(projectId, cancellationToken);
            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectId}");
            }

            // 2. Create indexing job
            job = IndexingJob.Create(projectId, totalFiles: 0); // Will update after planning
            await IndexingJob.UpsertAsync(job, cancellationToken);

            _logger.LogInformation(
                "Starting indexing for project {ProjectId} at path {Path} (Job: {JobId})",
                projectId,
                project.RootPath,
                job.Id);

            // Get commit SHA for provenance
            var commitSha = await _discovery.GetCommitShaAsync(project.RootPath);

            // 3. Plan differential scan
            var plan = await PlanIndexingAsync(projectId, project.RootPath, cancellationToken);

            // 4. Update job with plan statistics
            job.Status = JobStatus.Indexing;
            job.TotalFiles = plan.TotalFilesToProcess;
            job.NewFiles = plan.NewFiles.Count;
            job.ChangedFiles = plan.ChangedFiles.Count;
            job.MetadataOnlyFiles = plan.MetadataOnlyFiles.Count;
            job.SkippedFiles = plan.SkippedFiles.Count;
            job.CurrentOperation = $"Indexing {plan.TotalFilesToProcess} files...";
            await IndexingJob.UpsertAsync(job, cancellationToken);

            _logger.LogInformation(
                "Plan: {NewFiles} new, {ChangedFiles} changed, {MetadataOnly} metadata-only, " +
                "{Skipped} skipped, {Deleted} deleted (saved ~{Savings:F1}s)",
                plan.NewFiles.Count,
                plan.ChangedFiles.Count,
                plan.MetadataOnlyFiles.Count,
                plan.SkippedFiles.Count,
                plan.DeletedFiles.Count,
                plan.EstimatedTimeSavings.TotalSeconds);

            // Set partition context for this project (adapters handle partition formatting)
            using (EntityContext.Partition(projectId))
            {
                // Validate partition context
                var currentPartition = EntityContext.Current?.Partition;
                if (currentPartition != projectId)
                {
                    throw new InvalidOperationException(
                        $"Partition context mismatch: expected '{projectId}', got '{currentPartition ?? "(null)"}'");
                }

                _logger.LogDebug("Partition context set to project {ProjectId}", projectId);

                // 5. Process files that need indexing (new + changed)
                var filesToIndex = plan.NewFiles.Concat(plan.ChangedFiles).ToList();
                var filesProcessed = 0;
                var chunksCreated = 0;
                var vectorsSaved = 0;
                var batch = new List<(string Id, float[] Embedding, object? Metadata)>();

                foreach (var file in filesToIndex)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Report progress (update every 10 files to reduce DB writes)
                        if (filesProcessed % 10 == 0)
                        {
                            _logger.LogInformation(
                                "📊 [DEBUG] Progressive update triggered at file {FileCount}/{Total}",
                                filesProcessed,
                                filesToIndex.Count);

                            job.UpdateProgress(filesProcessed, $"Indexing {file.RelativePath}");
                            job.ChunksCreated = chunksCreated;
                            job.VectorsSaved = vectorsSaved;

                            _logger.LogInformation("📊 [DEBUG] Calling GetActualChunkStatsAsync for progressive update...");
                            // Update project's live stats (still inside partition context)
                            var (currentCount, currentBytes) = await GetActualChunkStatsAsync(projectId, cancellationToken);

                            _logger.LogInformation(
                                "📊 [DEBUG] Retrieved stats: {Count} chunks, {Bytes:N0} bytes. Updating project...",
                                currentCount,
                                currentBytes);

                            project.DocumentCount = currentCount;
                            project.IndexedBytes = currentBytes;
                            project.UpdatedAt = DateTime.UtcNow;
                            project.Status = IndexingStatus.Indexing;
                            await Project.UpsertAsync(project, cancellationToken);

                            _logger.LogInformation(
                                "📊 [DEBUG] Project saved with DocumentCount={Count}, IndexedBytes={Bytes:N0}",
                                project.DocumentCount,
                                project.IndexedBytes);

                            // Exit partition context temporarily to update job in root table
                            using (EntityContext.Partition(null))
                            {
                                await IndexingJob.UpsertAsync(job, cancellationToken);
                            }
                        }

                        progress?.Report(new IndexingProgress(
                            FilesProcessed: filesProcessed,
                            FilesTotal: filesToIndex.Count,
                            ChunksCreated: chunksCreated,
                            VectorsSaved: vectorsSaved,
                            CurrentFile: file.RelativePath));

                        // Delete existing chunks if this is a changed file
                        if (plan.ChangedFiles.Contains(file))
                        {
                            await DeleteChunksForFileAsync(projectId, file.RelativePath, cancellationToken);
                        }

                        // Extract content
                        var extracted = await _extraction.ExtractAsync(
                            file.AbsolutePath,
                            file.RelativePath,
                            cancellationToken);

                        var fileChunks = 0;

                        // Chunk content
                        await foreach (var chunk in _chunking.ChunkAsync(
                            extracted,
                            projectId.ToString(),
                            commitSha,
                            cancellationToken))
                        {
                            var provenance = ComputeProvenance(extracted.FullText, chunk.StartOffset, chunk.EndOffset);

                            // Generate embedding
                            var embedding = await _embedding.EmbedAsync(chunk.Text, cancellationToken);

                            // Create DocumentChunk entity
                            var docChunk = DocumentChunk.Create(
                                projectId: chunk.ProjectId,
                                filePath: chunk.FilePath,
                                searchText: chunk.Text,
                                tokenCount: chunk.TokenCount,
                                commitSha: commitSha,
                                title: chunk.Title,
                                language: chunk.Language);

                            docChunk.StartByteOffset = provenance.StartByteOffset;
                            docChunk.EndByteOffset = provenance.EndByteOffset;
                            docChunk.StartLine = provenance.StartLine;
                            docChunk.EndLine = provenance.EndLine;

                            // Save to relational store
                            await DocumentChunk.UpsertAsync(docChunk, cancellationToken);

                            // Add to vector batch
                            batch.Add((
                                Id: docChunk.Id,
                                Embedding: embedding,
                                Metadata: new
                                {
                                    docChunk.ProjectId,
                                    docChunk.FilePath,
                                    docChunk.SearchText,
                                    docChunk.CommitSha,
                                    docChunk.StartByteOffset,
                                    docChunk.EndByteOffset,
                                    docChunk.StartLine,
                                    docChunk.EndLine,
                                    docChunk.SourceUrl,
                                    docChunk.Title,
                                    docChunk.Language
                                }));

                            chunksCreated++;
                            fileChunks++;

                            // Save batch when it reaches target size
                            if (batch.Count >= BatchSize)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await SaveVectorBatchAsync(batch, cancellationToken);
                                vectorsSaved += batch.Count;
                                batch.Clear();

                                _logger.LogDebug("Saved batch of {Count} vectors", BatchSize);
                            }
                        }

                        // Update IndexedFile manifest
                        var fileInfo = new FileInfo(file.AbsolutePath);
                        var fileHash = await ComputeFileHashAsync(file.AbsolutePath, cancellationToken);

                        var indexedFileResults = await IndexedFile.Query(
                            f => f.ProjectId == projectId && f.RelativePath == file.RelativePath,
                            cancellationToken);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile == null)
                        {
                            indexedFile = IndexedFile.Create(
                                projectId,
                                file.RelativePath,
                                fileHash,
                                fileInfo.LastWriteTimeUtc,
                                fileInfo.Length,
                                fileChunks);
                        }
                        else
                        {
                            indexedFile.UpdateAfterIndexing(
                                fileHash,
                                fileInfo.LastWriteTimeUtc,
                                fileInfo.Length,
                                fileChunks);
                        }

                        await IndexedFile.UpsertAsync(indexedFile, cancellationToken);

                        filesProcessed++;
                        job.ProcessedFiles = filesProcessed;
                        job.ChunksCreated = chunksCreated;
                        job.VectorsSaved = vectorsSaved;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error indexing file {FilePath}", file.RelativePath);
                        errors.Add(new IndexingError(
                            FilePath: file.RelativePath,
                            ErrorMessage: ex.Message,
                            ErrorType: ex.GetType().Name,
                            StackTrace: ex.StackTrace));
                        job.ErrorFiles++;
                    }
                }

                // Save remaining vectors
                if (batch.Count > 0)
                {
                    await SaveVectorBatchAsync(batch, cancellationToken);
                    vectorsSaved += batch.Count;
                    job.VectorsSaved = vectorsSaved;
                }

                // 6. Handle metadata-only updates
                foreach (var file in plan.MetadataOnlyFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file.AbsolutePath);
                        var indexedFileResults = await IndexedFile.Query(
                            f => f.ProjectId == projectId && f.RelativePath == file.RelativePath,
                            cancellationToken);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile != null)
                        {
                            indexedFile.LastModified = fileInfo.LastWriteTimeUtc;
                            indexedFile.SizeBytes = fileInfo.Length;
                            await IndexedFile.UpsertAsync(indexedFile, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update metadata for {Path}", file.RelativePath);
                    }
                }

                // 7. Handle deletions
                foreach (var deletedPath in plan.DeletedFiles)
                {
                    try
                    {
                        await DeleteChunksForFileAsync(projectId, deletedPath, cancellationToken);

                        // Remove from manifest
                        var indexedFileResults = await IndexedFile.Query(
                            f => f.ProjectId == projectId && f.RelativePath == deletedPath,
                            cancellationToken);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile != null)
                        {
                            await indexedFile.Delete(cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete chunks for {Path}", deletedPath);
                    }
                }
            }

            // 8. Query actual chunk count and bytes from vector store
            _logger.LogInformation("🏁 [DEBUG] FINAL COMPLETION: Calling GetActualChunkStatsAsync...");
            var (actualCount, actualBytes) = await GetActualChunkStatsAsync(projectId, cancellationToken);

            _logger.LogInformation(
                "🏁 [DEBUG] FINAL STATS: {Count} chunks, {Bytes:N0} bytes. Calling MarkIndexed()...",
                actualCount,
                actualBytes);

            // 9. Update project metadata with actual values from database
            project.MarkIndexed(actualCount, actualBytes);

            _logger.LogInformation(
                "🏁 [DEBUG] After MarkIndexed: DocumentCount={Count}, IndexedBytes={Bytes:N0}, Status={Status}",
                project.DocumentCount,
                project.IndexedBytes,
                project.Status);

            await Project.UpsertAsync(project, cancellationToken);

            _logger.LogInformation(
                "🏁 [DEBUG] Project saved with final stats: DocumentCount={Count}, IndexedBytes={Bytes:N0}",
                project.DocumentCount,
                project.IndexedBytes);

            // 10. Mark job as completed
            job.Complete();

            // Exit partition context to update job in root table
            using (EntityContext.Partition(null))
            {
                await IndexingJob.UpsertAsync(job, cancellationToken);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Indexing complete: {FilesProcessed} files, {ChunksCreated} chunks, {VectorsSaved} vectors in {Duration} (Job: {JobId})",
                job.ProcessedFiles,
                job.ChunksCreated,
                job.VectorsSaved,
                stopwatch.Elapsed,
                job.Id);

            return new IndexingResult(
                FilesProcessed: job.ProcessedFiles,
                ChunksCreated: job.ChunksCreated,
                VectorsSaved: job.VectorsSaved,
                Duration: stopwatch.Elapsed,
                Errors: errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing failed for project {ProjectId}", projectId);
            stopwatch.Stop();

            // Mark job as failed
            if (job != null)
            {
                job.Fail(ex.Message);

                // Exit partition context to update job in root table
                using (EntityContext.Partition(null))
                {
                    await IndexingJob.UpsertAsync(job, cancellationToken);
                }
            }

            errors.Add(new IndexingError(
                FilePath: "(global)",
                ErrorMessage: ex.Message,
                ErrorType: ex.GetType().Name,
                StackTrace: ex.StackTrace));

            return new IndexingResult(
                FilesProcessed: job?.ProcessedFiles ?? 0,
                ChunksCreated: job?.ChunksCreated ?? 0,
                VectorsSaved: job?.VectorsSaved ?? 0,
                Duration: stopwatch.Elapsed,
                Errors: errors);
        }
    }

    /// <summary>
    /// Saves a batch of vectors with retry logic
    /// QA Issue #2 FIX: Uses Polly retry policy for resilience
    /// QA Issue #29 FIX: Instance method with logger access
    /// </summary>
    private async Task SaveVectorBatchAsync(
        List<(string Id, float[] Embedding, object? Metadata)> batch,
        CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("Saving batch of {Count} vectors to partition-aware storage", batch.Count);

            // Use Vector<DocumentChunk> to save embeddings
            // The partition context is already set by the outer scope
            await Vector<DocumentChunk>.Save(batch, cancellationToken);

            _logger.LogDebug("Successfully saved batch of {Count} vectors", batch.Count);
        });
    }

    /// <summary>
    /// Plans differential indexing by comparing discovered files against manifest
    /// </summary>
    /// <remarks>
    /// Three-tier change detection:
    /// 1. New files (not in manifest) → NewFiles
    /// 2. Changed files (hash mismatch) → ChangedFiles
    /// 3. Metadata-only (timestamp changed, hash same) → MetadataOnlyFiles
    /// 4. Unchanged (timestamp same) → SkippedFiles
    /// 5. Deleted (in manifest, not on disk) → DeletedFiles
    /// </remarks>
    private async Task<IndexingPlan> PlanIndexingAsync(
        string projectId,
        string projectRootPath,
        CancellationToken cancellationToken)
    {
        var planningStopwatch = Stopwatch.StartNew();
        var plan = new IndexingPlan();

        _logger.LogInformation("Planning differential scan for project {ProjectId}", projectId);

        // 1. Load existing manifest
        var existingFiles = await IndexedFile.Query(
            f => f.ProjectId == projectId,
            cancellationToken);

        var manifest = existingFiles.ToDictionary(
            f => f.RelativePath,
            StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug("Loaded manifest with {Count} files", manifest.Count);

        // 2. Discover current files
        var discoveredFiles = await _discovery
            .DiscoverAsync(projectRootPath, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Discovered {Count} files on disk", discoveredFiles.Count);

        // 3. Categorize each discovered file
        foreach (var file in discoveredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file.AbsolutePath);
            var relativePath = file.RelativePath;

            if (!manifest.TryGetValue(relativePath, out var existing))
            {
                // New file
                plan.NewFiles.Add(file);
                _logger.LogTrace("New file: {Path}", relativePath);
                continue;
            }

            // File exists in manifest - check if changed
            if (fileInfo.LastWriteTimeUtc == existing.LastModified &&
                fileInfo.Length == existing.SizeBytes)
            {
                // Fast path: timestamp and size unchanged → skip
                plan.SkippedFiles.Add(file);
                _logger.LogTrace("Skipped (unchanged): {Path}", relativePath);
                continue;
            }

            // Slow path: compute hash to detect content changes
            var currentHash = await ComputeFileHashAsync(file.AbsolutePath, cancellationToken);

            if (currentHash == existing.FileHash)
            {
                // Metadata changed but content is same
                plan.MetadataOnlyFiles.Add(file);
                _logger.LogTrace("Metadata-only change: {Path}", relativePath);
            }
            else
            {
                // Content changed
                plan.ChangedFiles.Add(file);
                _logger.LogTrace("Changed file: {Path}", relativePath);
            }
        }

        // 4. Find deleted files
        var discoveredPaths = new HashSet<string>(
            discoveredFiles.Select(f => f.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in manifest.Keys)
        {
            if (!discoveredPaths.Contains(manifestPath))
            {
                plan.DeletedFiles.Add(manifestPath);
                _logger.LogTrace("Deleted file: {Path}", manifestPath);
            }
        }

        planningStopwatch.Stop();
        plan.PlanningTime = planningStopwatch.Elapsed;

        // 5. Estimate time savings
        // Assume average processing time per file (can be refined with historical data)
        var avgTimePerFile = TimeSpan.FromMilliseconds(500);
        var filesSkipped = plan.SkippedFiles.Count + plan.MetadataOnlyFiles.Count;
        plan.EstimatedTimeSavings = avgTimePerFile * filesSkipped;

        _logger.LogInformation(
            "Planning complete: {Plan} (saved ~{Savings:F1}s)",
            plan,
            plan.EstimatedTimeSavings.TotalSeconds);

        return plan;
    }

    /// <summary>
    /// Deletes all chunks and vectors for a file
    /// </summary>
    private async Task DeleteChunksForFileAsync(
        string projectId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var chunks = await DocumentChunk.Query(
            c => c.ProjectId == projectId && c.FilePath == relativePath,
            cancellationToken);

        foreach (var chunk in chunks)
        {
            try
            {
                await Vector<DocumentChunk>.Delete(chunk.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete vector for chunk {ChunkId}", chunk.Id);
            }

            await chunk.Delete(cancellationToken);
        }

        _logger.LogDebug("Deleted chunks for file {Path}", relativePath);
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

    /// <summary>
    /// Computes SHA256 hash of a file
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fileStream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Queries actual chunk count and vector storage size from Weaviate
    /// </summary>
    /// <remarks>
    /// Provides accurate counters by querying actual state instead of relying on run-time counters
    /// </remarks>
    private async Task<(int count, long bytes)> GetActualChunkStatsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            // DEBUG: Track partition context
            var currentPartition = EntityContext.Current?.Partition;
            _logger.LogInformation(
                "🔍 [DEBUG] GetActualChunkStatsAsync START - ProjectId={ProjectId}, Partition={Partition}",
                projectId,
                currentPartition ?? "(null)");

            // Query all chunks in the current partition (partition = project boundary)
            // Note: We're already in the partition context when this is called
            _logger.LogInformation("🔍 [DEBUG] Calling DocumentChunk.All()...");
            var chunks = await DocumentChunk.All(cancellationToken);

            _logger.LogInformation("🔍 [DEBUG] Query returned, counting chunks...");
            var chunkList = chunks.ToList();
            var count = chunkList.Count;

            _logger.LogInformation(
                "🔍 [DEBUG] Materialized {Count} chunks from query",
                count);

            // Estimate bytes: each vector is 1536 dimensions * 4 bytes per float
            // Plus metadata overhead (estimate 1KB per chunk)
            var vectorBytes = count * 1536 * sizeof(float);
            var metadataBytes = count * 1024; // 1KB metadata per chunk
            var totalBytes = vectorBytes + metadataBytes;

            _logger.LogInformation(
                "🔍 [DEBUG] Actual stats for project {ProjectId}: {Count} chunks, ~{Bytes:N0} bytes",
                projectId,
                count,
                totalBytes);

            return (count, totalBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ [DEBUG] Failed to query actual chunk stats, returning zeros");
            return (0, 0);
        }
    }
}
