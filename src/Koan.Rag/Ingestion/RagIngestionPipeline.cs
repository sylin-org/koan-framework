using System.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Ingestion;

/// <summary>
/// Orchestrates the multi-stage ingestion pipeline: modality detection,
/// parallel extraction, entity/relationship extraction, chunking, embedding,
/// and concept graph construction.
/// </summary>
internal sealed class RagIngestionPipeline : IRagIngestionPipeline
{
    private readonly ILogger<RagIngestionPipeline> _logger;

    public RagIngestionPipeline(ILogger<RagIngestionPipeline> logger)
    {
        _logger = logger;
    }

    public async Task<RagIngestResult> IngestFiles<TEntity>(
        IReadOnlyList<string> filePaths,
        RagCorpusMetadata metadata,
        IProgress<RagIngestProgress>? progress,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();
        var processed = 0;
        var chunksCreated = 0;
        var entitiesExtracted = 0;
        var errors = new List<RagIngestError>();

        for (var i = 0; i < filePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = filePaths[i];
            try
            {
                var result = await IngestSingleFile<TEntity>(filePath, metadata, ct);
                chunksCreated += result.ChunksCreated;
                entitiesExtracted += result.EntitiesExtracted;
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to ingest file {FilePath}", filePath);
                errors.Add(new RagIngestError(filePath, ex.Message, ex));
            }

            progress?.Report(new RagIngestProgress(
                ProcessedFiles: i + 1,
                TotalFiles: filePaths.Count,
                ProcessedChunks: chunksCreated,
                CurrentFileName: Path.GetFileName(filePath)));
        }

        sw.Stop();

        return new RagIngestResult
        {
            FilesProcessed = processed,
            ChunksCreated = chunksCreated,
            EntitiesExtracted = entitiesExtracted,
            Errors = errors,
            Duration = sw.Elapsed
        };
    }

    public async Task<RagIngestResult> IngestEntity<TEntity>(
        TEntity entity,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Extract text content via EmbeddingMetadata convention inference
            var text = EntityAi.ExtractText(entity);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning(
                    "No embeddable content found on {EntityType} for RAG ingestion",
                    typeof(TEntity).Name);

                return new RagIngestResult
                {
                    FilesProcessed = 0,
                    Duration = sw.Elapsed
                };
            }

            // TODO Phase 1d: Full pipeline — chunk, embed, extract entities, build graph
            // For now: delegate to the existing embedding infrastructure
            var embedding = await Koan.AI.Client.Embed(text, ct);

            // Store in vector index
            await Koan.Data.Vector.Vector<TEntity>.Save(
                entity,
                new ReadOnlyMemory<float>(embedding),
                metadata: null,
                ct);

            sw.Stop();

            return new RagIngestResult
            {
                FilesProcessed = 1,
                ChunksCreated = 1, // Single-chunk for now; multi-chunk in Phase 1d
                EntitiesExtracted = 0, // Phase 2
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();

            return new RagIngestResult
            {
                FilesProcessed = 0,
                Errors = [new RagIngestError(typeof(TEntity).Name, ex.Message, ex)],
                Duration = sw.Elapsed
            };
        }
    }

    public Task RemoveEntity<TEntity>(
        TEntity entity,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // TODO: Remove chunks, decrement entity mentions, prune graph
        return Koan.Data.Vector.Vector<TEntity>.Delete(entity.Id, ct);
    }

    public Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // TODO Phase 4: Full rebuild with content-hash diffing
        _logger.LogInformation("Rebuild requested for corpus '{Corpus}'",
            metadata.EffectiveName(typeof(TEntity)));
        return Task.CompletedTask;
    }

    public Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        string? newDirective,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogInformation(
            "Rebuild with new directive requested for corpus '{Corpus}'",
            metadata.EffectiveName(typeof(TEntity)));
        return Task.CompletedTask;
    }

    public Task<RagCorpusStats> GetStats<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // TODO Phase 4: Real stats from chunk store, graph, and telemetry
        return Task.FromResult(new RagCorpusStats
        {
            FreshnessScore = 1.0
        });
    }

    public Task<bool> IsReady<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Ready if the vector store is available for this entity type
        return Task.FromResult(Koan.Data.Vector.Vector<TEntity>.IsAvailable);
    }

    public async Task Clear<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Clear the vector index
        if (Koan.Data.Vector.Vector<TEntity>.IsAvailable)
        {
            await Koan.Data.Vector.Vector<TEntity>.Clear(ct);
        }

        // TODO: Clear concept graph, chunk store, ingestion state
    }

    private async Task<(int ChunksCreated, int EntitiesExtracted)> IngestSingleFile<TEntity>(
        string filePath,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // TODO Phase 1d: Full file ingestion pipeline
        // 1. Detect modality from file extension / magic bytes
        // 2. Parse content (text, PDF, image, audio/video)
        // 3. Apply parallel extraction (Describe, OCR, Transcribe, Classify)
        // 4. Extract entities and facts
        // 5. Chunk with contextual prefixes and parent-child hierarchy
        // 6. Multi-level embed (document, section, chunk)
        // 7. Merge to concept graph

        _logger.LogDebug("Ingesting file {FilePath} into corpus", filePath);

        // Minimal implementation: read text, embed as single chunk
        var text = await File.ReadAllTextAsync(filePath, ct);
        if (string.IsNullOrWhiteSpace(text))
            return (0, 0);

        var embedding = await Koan.AI.Client.Embed(text, ct);

        // TODO: Store chunk with proper ID and metadata
        return (1, 0);
    }

}
