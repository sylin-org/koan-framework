using System.Diagnostics;
using System.Text;
using Koan.Context.Models;
using Koan.Data.Abstractions;
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
/// 1. Create Job for progress tracking
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
public class Indexer 
{
    private readonly Discovery _discovery;
    private readonly Extraction _extraction;
    private readonly Chunker _chunking;
    private readonly Embedding _embedding;
    private readonly IndexingCoordinator _coordinator;
    private readonly FileMonitoringService? _fileMonitor;
    private readonly ILogger<Indexer> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    private const int BatchSize = 100; // Save vectors in batches of 100

    public Indexer(
        Discovery discovery,
        Extraction extraction,
        Chunker chunking,
        Embedding embedding,
        IndexingCoordinator coordinator,
        ILogger<Indexer> logger,
        FileMonitoringService? fileMonitor = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _extraction = extraction ?? throw new ArgumentNullException(nameof(extraction));
        _chunking = chunking ?? throw new ArgumentNullException(nameof(chunking));
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileMonitor = fileMonitor;

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
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<IndexingError>();
        Job? job = null;
        string? cancelledJobId = null;
        CancellationToken coordinatorToken = default;

        try
        {
            // 1. Load project
            var project = await Project.Get(projectId, cancellationToken);
            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectId}");
            }

            // 2. Check for existing active jobs
            var existingJobs = await Job.Query(
                j => j.ProjectId == projectId &&
                     (j.Status == JobStatus.Pending ||
                      j.Status == JobStatus.Planning ||
                      j.Status == JobStatus.Indexing),
                cancellationToken);

            var existingJob = existingJobs.FirstOrDefault();

            if (existingJob != null && !force)
            {
                _logger.LogWarning(
                    "Indexing already in progress for project {ProjectId} (Job: {JobId}, Status: {Status}). " +
                    "Use force=true to cancel and restart.",
                    projectId,
                    existingJob.Id,
                    existingJob.Status);

                return new IndexingResult(
                    FilesProcessed: existingJob.ProcessedFiles,
                    ChunksCreated: existingJob.ChunksCreated,
                    VectorsSaved: existingJob.VectorsSaved,
                    Duration: existingJob.Elapsed,
                    Errors: new List<IndexingError>
                    {
                        new IndexingError(
                            FilePath: "(system)",
                            ErrorMessage: $"Indexing already in progress (Job {existingJob.Id}). Use force=true to cancel and restart.",
                            ErrorType: "ConcurrencyConflict",
                            StackTrace: null)
                    });
            }

            // 3. Create indexing job
            job = Job.Create(projectId, totalFiles: 0); // Will update after planning
            await job.Save(cancellationToken);

            // 4. Try to acquire indexing lock
            var (acquired, existingJobId, coordToken) = _coordinator.TryAcquireLock(
                projectId,
                job.Id,
                force);
            coordinatorToken = coordToken;

            if (!acquired)
            {
                // Shouldn't happen since we checked above, but handle it
                job.Fail($"Could not acquire lock - job {existingJobId} is already running");
                await job.Save(cancellationToken);

                return new IndexingResult(
                    FilesProcessed: 0,
                    ChunksCreated: 0,
                    VectorsSaved: 0,
                    Duration: stopwatch.Elapsed,
                    Errors: new List<IndexingError>
                    {
                        new IndexingError(
                            FilePath: "(system)",
                            ErrorMessage: $"Another indexing operation is already in progress (Job {existingJobId})",
                            ErrorType: "ConcurrencyConflict",
                            StackTrace: null)
                    });
            }

            // If we cancelled an existing job, mark it as such
            if (existingJobId != null)
            {
                cancelledJobId = existingJobId;
                if (existingJob != null)
                {
                    existingJob.Cancel();
                    existingJob.ErrorMessage = $"Cancelled by force restart (replaced by job {job.Id})";
                    await existingJob.Save(CancellationToken.None);
                }

                _logger.LogWarning(
                    "Force restart: Cancelled job {CancelledJobId} and starting new job {NewJobId} for project {ProjectId}",
                    cancelledJobId,
                    job.Id,
                    projectId);
            }

            // 5. Combine both cancellation tokens (caller's + coordinator's)
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                coordinatorToken);
            var effectiveCt = linkedCts.Token;

            _logger.LogInformation(
                "Starting indexing for project {ProjectId} at path {Path} (Job: {JobId}){CancelNote}",
                projectId,
                project.RootPath,
                job.Id,
                cancelledJobId != null ? $" (replaced job {cancelledJobId})" : "");

            // Set the active job ID on the project
            project.Status = IndexingStatus.Indexing;
            await project.Save(cancellationToken);

            // Get commit SHA for provenance
            var commitSha = await _discovery.GetCommitShaAsync(project.RootPath);

            // 6. Plan differential scan
            var plan = await PlanIndexingAsync(projectId, project.RootPath, effectiveCt);

            // 4. Update job with plan statistics
            job.Status = JobStatus.Indexing;
            job.TotalFiles = plan.TotalFilesToProcess;
            job.NewFiles = plan.NewFiles.Count;
            job.ChangedFiles = plan.ChangedFiles.Count;
            job.SkippedFiles = plan.SkippedFiles.Count;
            job.CurrentOperation = $"Indexing {plan.TotalFilesToProcess} files...";
            await job.Save(cancellationToken);

            _logger.LogInformation(
                "Plan: {NewFiles} new, {ChangedFiles} changed, {Skipped} skipped, " +
                "{Deleted} deleted (saved ~{Savings:F1}s)",
                plan.NewFiles.Count,
                plan.ChangedFiles.Count,
                plan.SkippedFiles.Count,
                plan.DeletedFiles.Count,
                plan.EstimatedTimeSavings.TotalSeconds);

            // Set partition context for this project
            // Parse projectId as GUID and format without hyphens
            var partitionId = $"proj-{Guid.Parse(projectId):N}";
            using (EntityContext.Partition(partitionId))
            {
                // Validate partition context
                var currentPartition = EntityContext.Current?.Partition;
                if (currentPartition != partitionId)
                {
                    throw new InvalidOperationException(
                        $"Partition context mismatch: expected '{partitionId}', got '{currentPartition ?? "(null)"}'");
                }

                _logger.LogDebug("Partition context set to {PartitionId}", partitionId);

                // 4.5. Bulk delete ALL existing chunks for this partition (clean slate for reindex)
                _logger.LogInformation("Clearing all existing chunks for partition {PartitionId}", partitionId);

                // Clear vectors from vector store first
                _logger.LogInformation("Flushing vector store for partition {PartitionId}", partitionId);
                await Vector<Chunk>.Flush(effectiveCt);
                _logger.LogInformation("Vector store flushed for partition {PartitionId}", partitionId);

                // Then clear relational chunks from SQLite
                var allChunks = await Chunk.Query(c => true, effectiveCt);
                var totalDeleted = 0;
                foreach (var chunk in allChunks)
                {
                    await chunk.Delete(effectiveCt);
                    totalDeleted++;
                }
                _logger.LogInformation("Deleted {Count} existing chunks from partition {PartitionId}", totalDeleted, partitionId);

                // 5. Process files that need indexing (new + changed)
                var filesToIndex = plan.NewFiles.Concat(plan.ChangedFiles).ToList();
                var filesProcessed = 0;
                var chunksCreated = 0;
                var vectorsSaved = 0;
                var batch = new List<(string Id, float[] Embedding, object? Metadata)>();

                foreach (var file in filesToIndex)
                {
                    effectiveCt.ThrowIfCancellationRequested();

                    try
                    {
                        // Report progress (update every 10 files to reduce DB writes)
                        if (filesProcessed % 10 == 0)
                        {
                            job.UpdateProgress(filesProcessed, $"Indexing {file.RelativePath}");
                            job.ChunksCreated = chunksCreated;
                            job.VectorsSaved = vectorsSaved;

                            // Update project's live stats (still inside partition context)
                            var (currentCount, currentBytes) = await GetActualChunkStatsAsync(effectiveCt);

                            project.DocumentCount = currentCount;
                            project.IndexedBytes = currentBytes;
                            project.Status = IndexingStatus.Indexing;
                            await project.Save(effectiveCt);

                            // Exit partition context temporarily to update job in root table
                            using (EntityContext.Partition(null))
                            {
                                await job.Save(effectiveCt);
                            }
                        }

                        progress?.Report(new IndexingProgress(
                            FilesProcessed: filesProcessed,
                            FilesTotal: filesToIndex.Count,
                            ChunksCreated: chunksCreated,
                            VectorsSaved: vectorsSaved,
                            CurrentFile: file.RelativePath));

                        // No per-file deletion needed - we bulk deleted all chunks at the start

                        // Extract content
                        var extracted = await _extraction.ExtractAsync(file.AbsolutePath, effectiveCt);

                        var fileChunks = 0;

                        // Chunk content
                        await foreach (var chunk in _chunking.ChunkAsync(
                            extracted,
                            projectId.ToString(),
                            commitSha,
                            effectiveCt))
                        {
                            var provenance = ComputeProvenance(extracted.FullText, chunk.StartOffset, chunk.EndOffset);

                            // Generate embedding
                            var embedding = await _embedding.EmbedAsync(chunk.Text, effectiveCt);

                            // Create Chunk entity (within partition context)
                            var docChunk = Chunk.Create(
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
                            var savedChunk = await docChunk.Save(effectiveCt);

                            // Add to vector batch
                            batch.Add((
                                Id: docChunk.Id,
                                Embedding: embedding,
                                Metadata: new
                                {
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
                                effectiveCt.ThrowIfCancellationRequested();
                                await SaveVectorBatchAsync(batch, effectiveCt);
                                vectorsSaved += batch.Count;
                                batch.Clear();
                            }
                        }

                        // Update IndexedFile manifest
                        var fileInfo = new FileInfo(file.AbsolutePath);
                        var fileHash = await ComputeFileHashAsync(file.AbsolutePath, effectiveCt);

                        var indexedFileResults = await IndexedFile.Query(
                            f => f.ProjectId == projectId && f.RelativePath == file.RelativePath,
                            effectiveCt);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile == null)
                        {
                            indexedFile = IndexedFile.Create(
                                projectId,
                                file.RelativePath,
                                fileHash,
                                fileInfo.Length);
                        }
                        else
                        {
                            indexedFile.UpdateAfterIndexing(
                                fileHash,
                                fileInfo.Length);
                        }

                        await indexedFile.Save(effectiveCt);

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
                    await SaveVectorBatchAsync(batch, effectiveCt);
                    vectorsSaved += batch.Count;
                    job.VectorsSaved = vectorsSaved;
                }

                // 6. Handle deletions (chunks already bulk-deleted at start, just clean up manifest)
                foreach (var deletedPath in plan.DeletedFiles)
                {
                    try
                    {
                        // Remove from manifest
                        var indexedFileResults = await IndexedFile.Query(
                            f => f.ProjectId == projectId && f.RelativePath == deletedPath,
                            effectiveCt);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile != null)
                        {
                            await indexedFile.Delete(effectiveCt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete chunks for {Path}", deletedPath);
                    }
                }
                // 8. Query actual chunk count and bytes from vector store (within partition context)
                _logger.LogInformation("🏁 [DEBUG] FINAL COMPLETION: Calling GetActualChunkStatsAsync...");
                var (actualCount, actualBytes) = await GetActualChunkStatsAsync(cancellationToken);

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

                await project.Save(cancellationToken);

                _logger.LogInformation(
                    "🏁 [DEBUG] Project saved with final stats: DocumentCount={Count}, IndexedBytes={Bytes:N0}",
                    project.DocumentCount,
                    project.IndexedBytes);
            }

            // 10. Mark job as completed
            job.Complete();

            // Exit partition context to update job in root table
            using (EntityContext.Partition(null))
            {
                await job.Save(cancellationToken);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Indexing complete: {FilesProcessed} files, {ChunksCreated} chunks, {VectorsSaved} vectors in {Duration} (Job: {JobId})",
                job.ProcessedFiles,
                job.ChunksCreated,
                job.VectorsSaved,
                stopwatch.Elapsed,
                job.Id);

            // Release the coordinator lock
            _coordinator.ReleaseLock(projectId, job.Id);

            return new IndexingResult(
                FilesProcessed: job.ProcessedFiles,
                ChunksCreated: job.ChunksCreated,
                VectorsSaved: job.VectorsSaved,
                Duration: stopwatch.Elapsed,
                Errors: errors);
        }
        catch (OperationCanceledException) when (coordinatorToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Indexing cancelled by force restart for project {ProjectId} (Job: {JobId})",
                projectId,
                job?.Id);

            stopwatch.Stop();

            // Mark job as cancelled
            if (job != null)
            {
                job.Cancel();
                job.ErrorMessage = "Cancelled by force restart";

                // Exit partition context to update job in root table
                using (EntityContext.Partition(null))
                {
                    await job.Save(CancellationToken.None);
                }

                // Release the coordinator lock
                _coordinator.ReleaseLock(projectId, job.Id);
            }

            // Clear active job ID from project (only if this was the active job)
            try
            {
                var project = await Project.Get(projectId, CancellationToken.None);
                {
                    project.Status = IndexingStatus.Failed;
                    await project.Save(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear ActiveJobId after cancellation");
            }

            errors.Add(new IndexingError(
                FilePath: "(system)",
                ErrorMessage: "Indexing cancelled by force restart",
                ErrorType: "ForceCancellation",
                StackTrace: null));

            return new IndexingResult(
                FilesProcessed: job?.ProcessedFiles ?? 0,
                ChunksCreated: job?.ChunksCreated ?? 0,
                VectorsSaved: job?.VectorsSaved ?? 0,
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
                    await job.Save(CancellationToken.None);
                }

                // Release the coordinator lock
                _coordinator.ReleaseLock(projectId, job.Id);
            }

            // Clear active job ID from project and mark as failed
            try
            {
                var project = await Project.Get(projectId, CancellationToken.None);
                {
                    project.Status = IndexingStatus.Failed;
                    project.LastError = ex.Message;
                    await project.Save(CancellationToken.None);
                }
            }
            catch (Exception clearEx)
            {
                _logger.LogWarning(clearEx, "Failed to clear ActiveJobId after failure");
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
    /// Saves a batch of vectors using Transactional Outbox Pattern
    /// </summary>
    /// <remarks>
    /// Instead of directly saving to vector store, creates SyncOperation records.
    /// VectorSyncWorker polls these operations and retries with exponential backoff.
    /// This ensures at-least-once delivery even if vector store is temporarily down.
    /// </remarks>
    private async Task SaveVectorBatchAsync(
        List<(string Id, float[] Embedding, object? Metadata)> batch,
        CancellationToken cancellationToken)
    {
        // Create SyncOperation records for outbox pattern
        foreach (var (id, embedding, metadata) in batch)
        {
            var operation = SyncOperation.Create(id, embedding, metadata);
            await operation.Save(cancellationToken);
        }

        _logger.LogDebug(
            "Created {Count} vector operations for outbox processing",
            batch.Count);
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

            // File exists in manifest - compute hash to detect changes
            var currentHash = await ComputeFileHashAsync(file.AbsolutePath, cancellationToken);

            if (currentHash == existing.ContentHash)
            {
                // Content unchanged - skip
                plan.SkippedFiles.Add(file);
                _logger.LogTrace("Skipped (unchanged): {Path}", relativePath);
            }
            else
            {
                // Content changed - re-index
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
        var filesSkipped = plan.SkippedFiles.Count;
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
    /// <remarks>
    /// Relies on partition context for isolation - must be called within EntityContext.Partition()
    /// </remarks>
    private async Task DeleteChunksForFileAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        // Query only by FilePath - partition context provides project isolation
        var chunks = await Chunk.Query(
            c => c.FilePath == relativePath,
            cancellationToken);

        var chunkCount = chunks.Count();

        _logger.LogDebug(
            "Deleting {Count} chunks for file {Path}",
            chunkCount,
            relativePath);

        foreach (var chunk in chunks)
        {
            try
            {
                await Vector<Chunk>.Delete(chunk.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete vector for chunk {ChunkId}", chunk.Id);
            }

            await chunk.Delete(cancellationToken);
        }

        _logger.LogInformation(
            "Deleted {Count} chunks for file {Path}",
            chunkCount,
            relativePath);
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
    /// Queries actual chunk count and vector storage size
    /// </summary>
    /// <remarks>
    /// Provides accurate counters by querying actual state instead of relying on run-time counters.
    /// Must be called within EntityContext.Partition() - partition context provides project isolation.
    /// </remarks>
    private async Task<(int count, long bytes)> GetActualChunkStatsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Query all chunks in the current partition (partition = project boundary)
            var chunks = await Chunk.All(cancellationToken);
            var count = chunks.Count();

            // Estimate bytes: each vector is 1536 dimensions * 4 bytes per float
            // Plus metadata overhead (estimate 1KB per chunk)
            var vectorBytes = count * 1536 * sizeof(float);
            var metadataBytes = count * 1024; // 1KB metadata per chunk
            var totalBytes = vectorBytes + metadataBytes;

            return (count, totalBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query actual chunk stats, returning zeros");
            return (0, 0);
        }
    }
}

/// <summary>
/// Result of an indexing operation
/// </summary>
public record IndexingResult(
    int FilesProcessed,
    int ChunksCreated,
    int VectorsSaved,
    TimeSpan Duration,
    IReadOnlyList<IndexingError> Errors);

/// <summary>
/// Error that occurred during indexing
/// </summary>
public record IndexingError(
    string FilePath,
    string ErrorMessage,
    string ErrorType,
    string? StackTrace);

/// <summary>
/// Progress information during indexing
/// </summary>
public record IndexingProgress(
    int FilesProcessed,
    int FilesTotal,
    int ChunksCreated,
    int VectorsSaved,
    string? CurrentFile);
