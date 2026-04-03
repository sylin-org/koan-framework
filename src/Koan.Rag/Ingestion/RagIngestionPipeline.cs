using System.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Rag.Abstractions;
using Koan.Rag.Chunking;
using Koan.Rag.Graph;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Ingestion;

/// <summary>
/// Orchestrates the multi-stage ingestion pipeline: text extraction,
/// contextual chunking, entity/relationship extraction, embedding,
/// and concept graph construction.
/// </summary>
internal sealed class RagIngestionPipeline : IRagIngestionPipeline
{
    private readonly ContextualChunker _chunker;
    private readonly EntityExtractor _entityExtractor;
    private readonly EntityResolver _entityResolver;
    private readonly IConceptGraphStore _graphStore;
    private readonly ILogger<RagIngestionPipeline> _logger;
    private readonly RagOptions _options;

    public RagIngestionPipeline(
        ContextualChunker chunker,
        EntityExtractor entityExtractor,
        EntityResolver entityResolver,
        IConceptGraphStore graphStore,
        IOptions<RagOptions> options,
        ILogger<RagIngestionPipeline> logger)
    {
        _chunker = chunker;
        _entityExtractor = entityExtractor;
        _entityResolver = entityResolver;
        _graphStore = graphStore;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<RagIngestResult> IngestFiles<TEntity>(
        IReadOnlyList<string> filePaths,
        RagCorpusMetadata metadata,
        IProgress<RagIngestProgress>? progress,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var sw = Stopwatch.StartNew();
        var processed = 0;
        var totalChunks = 0;
        var totalEntities = 0;
        var errors = new List<RagIngestError>();

        for (var i = 0; i < filePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = filePaths[i];

            try
            {
                var result = await IngestSingleFile<TEntity>(filePath, metadata, ct);
                totalChunks += result.ChunksCreated;
                totalEntities += result.EntitiesExtracted;
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
                ProcessedChunks: totalChunks,
                CurrentFileName: Path.GetFileName(filePath)));
        }

        // Persist graph snapshot after batch ingestion
        if (totalEntities > 0)
            await _graphStore.Save(ct);

        sw.Stop();
        return new RagIngestResult
        {
            FilesProcessed = processed,
            ChunksCreated = totalChunks,
            EntitiesExtracted = totalEntities,
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
            var text = EntityAi.ExtractText(entity);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning(
                    "No content found on {EntityType} for RAG ingestion", typeof(TEntity).Name);
                return new RagIngestResult { Duration = sw.Elapsed };
            }

            var documentId = entity.Id;
            var documentTitle = ExtractTitle(entity);

            // 1. Contextual chunking with parent-child hierarchy
            var chunked = await _chunker.Chunk(text, documentTitle, metadata.Directive, ct);

            // 2. Embed child chunks and store in vector index
            var chunksCreated = 0;
            foreach (var child in chunked.ChildChunks)
            {
                ct.ThrowIfCancellationRequested();

                var embedding = await Koan.AI.Client.Embed(child.Text, ct);

                // Store chunk embedding with metadata linking to document and parent
                await Koan.Data.Vector.Vector<TEntity>.Save(
                    $"{documentId}:{child.Id}",
                    embedding,
                    new Dictionary<string, object>
                    {
                        ["document_id"] = documentId,
                        ["parent_id"] = child.ParentId ?? "",
                        ["section"] = child.SectionTitle ?? "",
                        ["is_child"] = true
                    },
                    ct);

                chunksCreated++;
            }

            // 3. Extract entities from each parent chunk and resolve
            var entitiesExtracted = 0;
            if (metadata.GraphStrategy != GraphStrategy.Lazy)
            {
                entitiesExtracted = await ExtractAndResolveEntities(
                    chunked.ParentChunks, documentId, documentTitle, metadata, ct);
            }

            sw.Stop();
            return new RagIngestResult
            {
                FilesProcessed = 1,
                ChunksCreated = chunksCreated,
                EntitiesExtracted = entitiesExtracted,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return new RagIngestResult
            {
                Errors = [new RagIngestError(typeof(TEntity).Name, ex.Message, ex)],
                Duration = sw.Elapsed
            };
        }
    }

    public async Task RemoveEntity<TEntity>(
        TEntity entity,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Remove chunks from vector index
        await Koan.Data.Vector.Vector<TEntity>.Delete(entity.Id, ct);

        // TODO: Remove all child chunks (need chunk store to enumerate)
        // TODO: Build removal delta for concept graph
    }

    public Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogInformation("Rebuild requested for corpus '{Corpus}'",
            metadata.EffectiveName(typeof(TEntity)));
        // TODO: Full rebuild — query all entities, re-ingest with content-hash diffing
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
        var graphStats = _graphStore.GetStats();
        return Task.FromResult(new RagCorpusStats
        {
            Entities = graphStats.EntityCount,
            Relationships = graphStats.RelationshipCount,
            FreshnessScore = 1.0,
            LastFullReindex = graphStats.LastPersisted
        });
    }

    public Task<bool> IsReady<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        return Task.FromResult(Koan.Data.Vector.Vector<TEntity>.IsAvailable);
    }

    public async Task Clear<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        if (Koan.Data.Vector.Vector<TEntity>.IsAvailable)
            await Koan.Data.Vector.Vector<TEntity>.Clear(ct);

        // Clear concept graph
        await _graphStore.ApplyDelta(new GraphDelta
        {
            // Full clear via empty delta — the store handles this
            // TODO: Add a Clear() method to IConceptGraphStore
        }, ct);
    }

    // ── Internal Pipeline Steps ─────────────────────────────────────────

    private async Task<(int ChunksCreated, int EntitiesExtracted)> IngestSingleFile<TEntity>(
        string filePath,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogDebug("Ingesting file {FilePath}", filePath);

        // Read file content
        // TODO: Modality detection + multi-modal extraction (OCR, Describe, Transcribe)
        var text = await File.ReadAllTextAsync(filePath, ct);
        if (string.IsNullOrWhiteSpace(text))
            return (0, 0);

        var documentId = Path.GetFileNameWithoutExtension(filePath);
        var documentTitle = Path.GetFileName(filePath);

        // Chunk
        var chunked = await _chunker.Chunk(text, documentTitle, metadata.Directive, ct);

        // Embed child chunks
        var chunksCreated = 0;
        foreach (var child in chunked.ChildChunks)
        {
            ct.ThrowIfCancellationRequested();

            var embedding = await Koan.AI.Client.Embed(child.Text, ct);
            // TODO: Store in chunk-specific vector collection
            chunksCreated++;
        }

        // Extract entities
        var entitiesExtracted = 0;
        if (metadata.GraphStrategy != GraphStrategy.Lazy)
        {
            entitiesExtracted = await ExtractAndResolveEntities(
                chunked.ParentChunks, documentId, documentTitle, metadata, ct);
        }

        return (chunksCreated, entitiesExtracted);
    }

    private async Task<int> ExtractAndResolveEntities(
        IReadOnlyList<RagContentChunk> parentChunks,
        string documentId,
        string? documentTitle,
        RagCorpusMetadata metadata,
        CancellationToken ct)
    {
        var totalEntities = 0;

        foreach (var parent in parentChunks)
        {
            ct.ThrowIfCancellationRequested();

            // Extract entities from this chunk
            var extraction = await _entityExtractor.ExtractEntities(
                parent.Text, documentTitle, parent.SectionTitle, metadata.Directive, ct);

            if (extraction.Entities.Count == 0)
                continue;

            // Resolve against existing graph
            var resolution = await _entityResolver.Resolve(
                extraction.Entities, documentId, ct);

            // Apply delta to graph
            await _graphStore.ApplyDelta(resolution.Delta, ct);
            totalEntities += resolution.ResolvedEntities.Count;

            // Full strategy: also extract explicit relationships
            if (metadata.GraphStrategy == GraphStrategy.Full && resolution.ResolvedEntities.Count >= 2)
            {
                var entityNames = resolution.ResolvedEntities
                    .Select(e => e.CanonicalName)
                    .ToList();

                var relationships = await _entityExtractor.ExtractRelationships(
                    parent.Text, entityNames, metadata.Directive, ct);

                if (relationships.Relationships.Count > 0)
                {
                    var relDelta = BuildRelationshipDelta(
                        relationships.Relationships, resolution.ResolvedEntities, documentId);
                    await _graphStore.ApplyDelta(relDelta, ct);
                }
            }
        }

        return totalEntities;
    }

    private static GraphDelta BuildRelationshipDelta(
        IReadOnlyList<ExtractedRelationship> relationships,
        IReadOnlyList<ConceptEntity> resolvedEntities,
        string documentId)
    {
        var entityLookup = resolvedEntities.ToDictionary(
            e => e.CanonicalName,
            e => e.Id,
            StringComparer.OrdinalIgnoreCase);

        var addedRelationships = new List<ConceptRelationship>();

        foreach (var rel in relationships)
        {
            if (entityLookup.TryGetValue(
                    EntityResolver.NormalizeName(rel.From), out var fromId) &&
                entityLookup.TryGetValue(
                    EntityResolver.NormalizeName(rel.To), out var toId))
            {
                addedRelationships.Add(new ConceptRelationship
                {
                    Id = $"rel:{fromId}:{toId}:{rel.Label.ToLowerInvariant().Replace(' ', '-')}",
                    FromEntityId = fromId,
                    ToEntityId = toId,
                    Label = rel.Label,
                    Confidence = 1.0,
                    SourceDocumentId = documentId
                });
            }
        }

        return new GraphDelta { AddedRelationships = addedRelationships };
    }

    private static string? ExtractTitle<TEntity>(TEntity entity) where TEntity : class
    {
        // Try common title properties by convention
        var titleProp = typeof(TEntity).GetProperty("Title")
            ?? typeof(TEntity).GetProperty("Name")
            ?? typeof(TEntity).GetProperty("Subject");

        return titleProp?.GetValue(entity)?.ToString();
    }
}
