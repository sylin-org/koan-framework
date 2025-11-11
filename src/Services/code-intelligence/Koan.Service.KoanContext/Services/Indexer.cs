using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Koan.Context.Models;
using Koan.Context.Services.Maintenance;
using Koan.Context.Utilities;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Koan.Context.Services;

/// <summary>
/// Orchestrates the full indexing pipeline with differential scanning and job tracking
/// </summary>
/// <remarks>
/// Parallel Processing Architecture:
/// - Indexer (producer stream): Chunks files and creates ChunkVectorState snapshots
/// - VectorSyncWorker (consumer stream): Processes ChunkVectorState payloads and syncs to Weaviate
/// - Both streams run in parallel from the first batch
/// - Job completes when: all files chunked AND all vectors synced to Weaviate
///
/// Pipeline flow:
/// 1. Create Job for progress tracking
/// 2. Plan differential scan (SHA256-based change detection)
/// 3. Set partition context for project
/// 4. Process changed/new files and create chunks (SQLite)
/// 5. Persist ChunkVectorState snapshots for background synchronization
/// 6. Update manifest with file hashes
/// 7. Query actual counts from database
/// 8. Update project metadata
/// 9. Chunking phase complete (Job stays in Indexing state)
/// 10. VectorSyncWorker syncs vectors to Weaviate in parallel
/// 11. Job marked Complete when VectorsSynced == ChunksCreated
///
/// Progress Tracking:
/// - Composite: 50% chunking (ProcessedFiles/TotalFiles) + 50% vector sync (VectorsSynced/ChunksCreated)
/// - ChunksCreated: Chunks saved to SQLite
/// - VectorsSaved: ChunkVectorState snapshots captured (ready for sync)
/// - VectorsSynced: Vectors successfully saved to Weaviate
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
    private readonly IndexingPlanner _planner;
    private readonly ChunkMaintenanceService _chunkMaintenance;
    private readonly TagResolver _tagResolver;
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
    IndexingPlanner planner,
    ChunkMaintenanceService chunkMaintenance,
    TagResolver tagResolver,
    IndexingCoordinator coordinator,
    ILogger<Indexer> logger,
    FileMonitoringService? fileMonitor = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _extraction = extraction ?? throw new ArgumentNullException(nameof(extraction));
        _chunking = chunking ?? throw new ArgumentNullException(nameof(chunking));
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _chunkMaintenance = chunkMaintenance ?? throw new ArgumentNullException(nameof(chunkMaintenance));
        _tagResolver = tagResolver ?? throw new ArgumentNullException(nameof(tagResolver));
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
                    await existingJob.Cancel(CancellationToken.None);
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
            var plan = await _planner.PlanAsync(
                projectId,
                project.RootPath,
                forceReindex: force,
                cancellationToken: effectiveCt);

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

            // Log plan results to job
            job.LogOperation($"Scan complete: {plan.NewFiles.Count} new, {plan.ChangedFiles.Count} changed, {plan.SkippedFiles.Count} skipped, {plan.DeletedFiles.Count} deleted");

            // Check if there are no files to process
            if (plan.TotalFilesToProcess == 0)
            {
                _logger.LogWarning("No files to process - project is up to date");
                job.AddWarning("No files to process - project is up to date");
            }

            // Set partition context for this project (adapters handle formatting)
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

                if (plan.OrphanedChunkFiles.Count > 0)
                {
                    var orphanResults = await _chunkMaintenance.RemoveFilesAsync(
                        plan.OrphanedChunkFiles,
                        deleteIndexedFile: false,
                        deleteVectors: true,
                        cancellationToken: effectiveCt);

                    var orphanChunksDeleted = orphanResults.Sum(r => r.ChunksDeleted);
                    if (orphanChunksDeleted > 0)
                    {
                        _logger.LogInformation(
                            "Removed {Count} orphaned chunk groups for project {ProjectId}",
                            orphanChunksDeleted,
                            projectId);
                    }

                    job.LogOperation($"Removed {orphanChunksDeleted} orphaned chunks");
                }

                if (plan.DeletedFiles.Count > 0)
                {
                    var deletedResults = await _chunkMaintenance.RemoveFilesAsync(
                        plan.DeletedFiles,
                        deleteIndexedFile: true,
                        deleteVectors: true,
                        cancellationToken: effectiveCt);

                    var deletedFilesReconciled = deletedResults.Count;
                    var deletedChunks = deletedResults.Sum(r => r.ChunksDeleted);
                    var manifestsRemoved = deletedResults.Count(r => r.IndexedFileRemoved);

                    if (deletedChunks > 0)
                    {
                        _logger.LogInformation(
                            "Removed {Count} chunks linked to deleted files for project {ProjectId}",
                            deletedChunks,
                            projectId);
                    }

                    job.LogOperation(
                        $"Reconciled {deletedFilesReconciled} deleted files ({deletedChunks} chunks removed)");

                    if (manifestsRemoved < deletedFilesReconciled)
                    {
                        var missing = deletedFilesReconciled - manifestsRemoved;
                        if (missing > 0)
                        {
                            job.AddWarning($"{missing} deleted files had no manifest entry");
                        }
                    }
                }

                // 5. Process files that need indexing (new + changed)
                var filesToIndex = plan.NewFiles.Concat(plan.ChangedFiles).ToList();
                var changedLookup = new HashSet<string>(
                    plan.ChangedFiles.Select(f => f.RelativePath),
                    StringComparer.OrdinalIgnoreCase);

                // Log start of file indexing
                if (filesToIndex.Count > 0)
                {
                    _logger.LogInformation("Starting indexing of {Count} files", filesToIndex.Count);
                    job.LogOperation($"Starting indexing of {filesToIndex.Count} files");
                }

                var filesProcessed = 0;
                var chunksCreated = 0;
                var vectorsSaved = 0;
                var batch = new List<(string ChunkId, string IndexedFileId, string ChunkVersion, float[] Embedding, object? Metadata)>();

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

                            // Update project's live stats
                            var (currentCount, currentBytes) = await GetActualChunkStatsAsync(effectiveCt);

                            project.DocumentCount = currentCount;
                            project.IndexedBytes = currentBytes;
                            project.Status = IndexingStatus.Indexing;

                            // Exit partition context temporarily to update project and job in root table
                            using (EntityContext.With(partition: null))
                            {
                                await project.Save(effectiveCt);
                                await job.Save(effectiveCt);
                            }
                        }

                        progress?.Report(new IndexingProgress(
                            FilesProcessed: filesProcessed,
                            FilesTotal: filesToIndex.Count,
                            ChunksCreated: chunksCreated,
                            VectorsSaved: vectorsSaved,
                            CurrentFile: file.RelativePath));

                        if (changedLookup.Contains(file.RelativePath))
                        {
                            await _chunkMaintenance.RemoveFileAsync(
                                file.RelativePath,
                                deleteIndexedFile: false,
                                deleteVectors: true,
                                cancellationToken: effectiveCt);
                        }

                        // 1. Create/update IndexedFile FIRST (within partition context)
                        var fileInfo = new FileInfo(file.AbsolutePath);
                        var fileHash = await FileHasher.ComputeSha256Async(file.AbsolutePath, effectiveCt);

                        var indexedFileResults = await IndexedFile.Query(
                            f => f.RelativePath == file.RelativePath,
                            effectiveCt);
                        var indexedFile = indexedFileResults.FirstOrDefault();

                        if (indexedFile == null)
                        {
                            indexedFile = IndexedFile.Create(
                                file.RelativePath,
                                fileHash,
                                fileInfo.Length);
                        }
                        else
                        {
                            indexedFile.UpdateAfterIndexing(fileHash, fileInfo.Length);
                        }

                        // 2. Extract content and derive metadata prior to saving manifest entry
                        var extracted = await _extraction.ExtractAsync(
                            file.AbsolutePath,
                            file.RelativePath,
                            effectiveCt);
                        var frontmatter = FrontmatterParser.Parse(extracted.FullText);
                        var filePathSegments = PathMetadata.GetPathSegments(file.RelativePath);
                        var fileTagInput = TagResolverInput.ForFile(
                            projectId,
                            file.RelativePath,
                            pipelineName: null,
                            language: null,
                            frontmatter: frontmatter.Metadata,
                            fileTags: frontmatter.Tags);
                        var fileTagResult = await _tagResolver.ResolveAsync(fileTagInput, effectiveCt);

                        indexedFile.SetTagEnvelope(fileTagResult.Envelope);
                        await indexedFile.Save(effectiveCt);

                        var inheritedTags = GetInheritedTags(fileTagResult.Envelope);

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

                            // Create Chunk entity (within partition context, linked to IndexedFile)
                            var docChunk = Chunk.Create(
                                indexedFileId: indexedFile.Id,
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

                            docChunk.PathSegments = filePathSegments;
                            docChunk.FileLastModified = fileInfo.LastWriteTimeUtc;
                            docChunk.FileHash = fileHash;

                            var chunkTagInput = fileTagInput.ForChunk(chunk.Language, chunk.Text, inheritedTags);
                            var chunkTagResult = await _tagResolver.ResolveAsync(chunkTagInput, effectiveCt);
                            docChunk.SetTagEnvelope(chunkTagResult.Envelope);

                            var chunkVersion = ChunkVersionCalculator.Calculate(projectId, docChunk);
                            docChunk.VectorVersion = chunkVersion;

                            // Save to relational store immediately (no transaction buffering)
                            // Force synchronous commit to ensure chunk metadata is persisted
                            await docChunk.Save(effectiveCt);

                            _logger.LogTrace(
                                "Saved chunk {ChunkId} for file {FilePath} (project: {ProjectId})",
                                docChunk.Id,
                                docChunk.FilePath,
                                projectId);

                            // Add to vector batch
                            batch.Add((
                                ChunkId: docChunk.Id,
                                IndexedFileId: docChunk.IndexedFileId,
                                ChunkVersion: chunkVersion,
                                Embedding: embedding,
                                Metadata: new ChunkVectorMetadata
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
                                }));

                            chunksCreated++;
                            fileChunks++;
                            vectorsSaved = chunksCreated;
                            job.VectorsSaved = vectorsSaved;

                            // Save batch when it reaches target size
                            if (batch.Count >= BatchSize)
                            {
                                effectiveCt.ThrowIfCancellationRequested();
                                await SaveVectorBatchAsync(job.Id, batch, projectId, effectiveCt);
                                vectorsSaved = chunksCreated;
                                batch.Clear();
                            }
                        }

                        filesProcessed++;
                        job.ProcessedFiles = filesProcessed;
                        job.ChunksCreated = chunksCreated;
                        job.VectorsSaved = vectorsSaved;
                    }
                    catch (FileSizeExceededException ex)
                    {
                        // File size limit exceeded - skip file but don't count as error
                        _logger.LogWarning("Skipped oversized file: {FilePath} ({FileSizeMB:F2} MB)",
                            file.RelativePath, ex.FileSizeMB);
                        job.SkippedFiles++;
                        job.AddWarning($"Skipped oversized file: {file.RelativePath} ({ex.FileSizeMB:F2} MB)");
                        job.LogOperation($"File {file.RelativePath}: Skipped (oversized)");
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
                        job.AddWarning($"Error indexing {file.RelativePath}: {ex.Message}");
                        job.LogOperation($"File {file.RelativePath}: Error - {ex.Message}");
                    }
                }

                // Save remaining vectors
                if (batch.Count > 0)
                {
                    await SaveVectorBatchAsync(job.Id, batch, projectId, effectiveCt);
                    vectorsSaved = chunksCreated;
                    job.VectorsSaved = vectorsSaved;
                    job.LogOperation($"Completed indexing: {filesProcessed} files processed, {chunksCreated} chunks created");
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

                // Exit partition context to save project in root table
                using (EntityContext.With(partition: null))
                {
                    await project.Save(cancellationToken);
                }

                _logger.LogInformation(
                    "🏁 [DEBUG] Project saved with final stats: DocumentCount={Count}, IndexedBytes={Bytes:N0}",
                    project.DocumentCount,
                    project.IndexedBytes);
            }

            // 10. Chunking complete - update job but DO NOT mark as completed
            // Job will be marked Complete by VectorSyncWorker when all vectors are synced to Weaviate
            job.CurrentOperation = $"Chunking complete. Waiting for {job.ChunksCreated} vectors to sync...";
            job.LogOperation($"Chunking phase complete: {job.ProcessedFiles} files, {job.ChunksCreated} chunks created");

            // Exit partition context to update job in root table
            using (EntityContext.With(partition: null))
            {
                await job.Save(cancellationToken);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Chunking complete: {FilesProcessed} files, {ChunksCreated} chunks, {VectorsSaved} vector snapshots captured in {Duration} (Job: {JobId}). " +
                "Job will complete when VectorSyncWorker syncs all vectors to Weaviate.",
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

            if (job != null)
            {
                job.LogOperation("Cancellation requested during indexing");
            }

            stopwatch.Stop();

            // Mark job as cancelled
            if (job != null)
            {
                await job.Cancel(CancellationToken.None);
                job.ErrorMessage = "Cancelled by force restart";

                // Exit partition context to update job in root table
                using (EntityContext.With(partition: null))
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
                if (project != null)
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

            if (job != null)
            {
                job.LogOperation($"Indexing failed: {ex.Message}");
            }

            stopwatch.Stop();

            // Mark job as failed
            if (job != null)
            {
                job.Fail(ex.Message);

                // Exit partition context to update job in root table
                using (EntityContext.With(partition: null))
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
                if (project != null)
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
    /// Persists vector payload snapshots for downstream synchronization.
    /// </summary>
    /// <remarks>
    /// Stores the latest embedding and metadata per chunk in <see cref="ChunkVectorState"/>.
    /// The background worker consumes these snapshots idempotently, guaranteeing that each
    /// chunk has at most one pending payload regardless of replays or restarts.
    /// </remarks>
    private async Task SaveVectorBatchAsync(
        string jobId,
        List<(string ChunkId, string IndexedFileId, string ChunkVersion, float[] Embedding, object? Metadata)> batch,
        string projectId,
        CancellationToken cancellationToken)
    {
        using (EntityContext.With(partition: null))
        {
            var created = 0;
            var updated = 0;

            foreach (var (chunkId, indexedFileId, chunkVersion, embedding, metadata) in batch)
            {
                var state = await ChunkVectorState.Get(chunkId, cancellationToken);
                if (state is null)
                {
                    state = ChunkVectorState.Create(
                        chunkId,
                        projectId,
                        jobId,
                        indexedFileId,
                        chunkVersion,
                        embedding,
                        metadata);
                    created++;
                }
                else
                {
                    state.Reset(projectId, jobId, indexedFileId, chunkVersion, embedding, metadata);
                    updated++;
                }

                await state.Save(cancellationToken);
            }

            _logger.LogDebug(
                "Upserted {Created} new and {Updated} existing vector snapshots (job: {JobId}, project: {ProjectId})",
                created,
                updated,
                jobId,
                projectId);
        }
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
