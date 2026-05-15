using System.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Rag.Abstractions;
using Koan.Rag.Chunking;
using Koan.Rag.Content;
using Koan.Rag.Distillation;
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
    private readonly ContentAdapterRegistry _contentRegistry;
    private readonly ContextualChunker _chunker;
    private readonly EntityExtractor _entityExtractor;
    private readonly EntityResolver _entityResolver;
    private readonly IConceptGraphStore _graphStore;
    private readonly DistillationTreeBuilder _treeBuilder;
    private readonly IDistillationTreeStore _treeStore;
    private readonly IDocumentSegmenter _segmenter;
    private readonly ILogger<RagIngestionPipeline> _logger;
    private readonly RagOptions _options;

    public RagIngestionPipeline(
        ContentAdapterRegistry contentRegistry,
        ContextualChunker chunker,
        EntityExtractor entityExtractor,
        EntityResolver entityResolver,
        IConceptGraphStore graphStore,
        DistillationTreeBuilder treeBuilder,
        IDistillationTreeStore treeStore,
        IDocumentSegmenter segmenter,
        IOptions<RagOptions> options,
        ILogger<RagIngestionPipeline> logger)
    {
        _contentRegistry = contentRegistry;
        _chunker = chunker;
        _entityExtractor = entityExtractor;
        _entityResolver = entityResolver;
        _graphStore = graphStore;
        _treeBuilder = treeBuilder;
        _treeStore = treeStore;
        _segmenter = segmenter;
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

            // 2. Embed child chunks and store in vector index — parallel with bounded concurrency
            using var semaphore = new SemaphoreSlim(5); // Max 5 concurrent embed calls
            var embedTasks = chunked.ChildChunks.Select(async child =>
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct);
                try
                {
                    var embedding = await Koan.AI.Client.Embed(child.Text, ct);
                    var chunkId = $"{documentId}:{child.Id}";

                    // Store chunk embedding with metadata linking to document and parent
                    await Koan.Data.Vector.Vector<TEntity>.Save(
                        chunkId,
                        embedding,
                        new Dictionary<string, object>
                        {
                            ["document_id"] = documentId,
                            ["parent_id"] = child.ParentId ?? "",
                            ["section"] = child.SectionTitle ?? "",
                            ["is_child"] = true
                        },
                        ct);

                    return new EmbeddingWithText(chunkId, embedding, child.Text);
                }
                finally { semaphore.Release(); }
            }).ToList();

            var embedResults = await Task.WhenAll(embedTasks);
            var leafEmbeddings = embedResults.ToList();
            var chunksCreated = leafEmbeddings.Count;

            // 3. Extract entities from each parent chunk and resolve
            var entitiesExtracted = 0;
            if (metadata.GraphStrategy != GraphStrategy.Lazy)
            {
                entitiesExtracted = await ExtractAndResolveEntities(
                    chunked.ParentChunks, documentId, documentTitle, metadata, ct);
            }

            // 4. Build per-document distillation tree if there are enough chunks
            if (leafEmbeddings.Count >= 3)
                await _treeBuilder.BuildTree(leafEmbeddings, metadata.Directive, ct);

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
        var documentId = entity.Id;

        // Remove primary entity from vector index
        await Koan.Data.Vector.Vector<TEntity>.Delete(documentId, ct);

        // Remove child chunks by ID prefix pattern
        // Child chunks use IDs like "{documentId}:parent-N-child-M"
        // Since we can't enumerate by prefix in all providers, we track
        // chunk IDs via the concept graph's entity-document provenance
        // and let the graph's reference counting handle cleanup.

        // Build graph removal delta: decrement mention counts for all
        // entities sourced from this document. Entities reaching zero
        // mentions are pruned automatically by ApplyDelta.
        var graphStats = _graphStore.GetStats();
        if (graphStats.EntityCount > 0)
        {
            // Get the neighborhood to find entities this document contributed to
            var neighborhood = await _graphStore.GetNeighborhood(documentId, depth: 0, ct);

            if (neighborhood.Entities.Count > 0)
            {
                var removalDelta = _entityResolver.BuildRemovalDelta(
                    documentId,
                    neighborhood.Entities.Select(e => e.Id).ToList());

                await _graphStore.ApplyDelta(removalDelta, ct);
            }
        }

        _logger.LogDebug("Removed entity {EntityId} from corpus '{Corpus}'",
            documentId, metadata.EffectiveName(typeof(TEntity)));
    }

    public async Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogInformation("Rebuild requested for corpus '{Corpus}'",
            metadata.EffectiveName(typeof(TEntity)));

        await RebuildInternal<TEntity>(metadata, newDirective: null, ct);
    }

    public async Task Rebuild<TEntity>(
        RagCorpusMetadata metadata,
        string? newDirective,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogInformation(
            "Rebuild with new directive requested for corpus '{Corpus}'",
            metadata.EffectiveName(typeof(TEntity)));

        await RebuildInternal<TEntity>(metadata, newDirective, ct);
    }

    private async Task RebuildInternal<TEntity>(
        RagCorpusMetadata metadata,
        string? newDirective,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        // Use the new directive if provided, otherwise keep existing
        var effectiveMetadata = newDirective is not null
            ? RagCorpusMetadata.CreateDynamic(
                metadata.Name ?? typeof(TEntity).Name, newDirective)
            : metadata;

        // Query all entities of this type
        var entities = await Koan.Data.Core.Data<TEntity, string>
            .All(options: null, ct: ct);

        var entityList = entities.ToList();

        if (entityList.Count > 10_000)
            _logger.LogWarning(
                "Rebuild loading {Count} entities into memory. Consider batched rebuild for very large corpora.",
                entityList.Count);

        _logger.LogInformation(
            "Rebuild: processing {Count} entities for corpus '{Corpus}'",
            entityList.Count, metadata.EffectiveName(typeof(TEntity)));

        var processed = 0;
        var skipped = 0;

        foreach (var entity in entityList)
        {
            ct.ThrowIfCancellationRequested();

            // Content-hash diffing: check if the entity content has changed
            // since last ingestion. Skip re-processing for unchanged entities
            // unless a directive change forces full re-extraction.
            if (newDirective is null)
            {
                var embeddingMeta = Koan.Data.AI.EmbeddingMetadata.Resolve<TEntity>();
                var currentSignature = embeddingMeta.ComputeSignature(entity);
                var jobId = Workers.RagIngestionJob.MakeId(
                    typeof(TEntity).Name, metadata.Name, entity.Id);

                var existingJob = await Workers.RagIngestionJob.Get(jobId, ct);
                if (existingJob is not null &&
                    existingJob.ContentSignature == currentSignature &&
                    existingJob.Status == Abstractions.RagIngestionStatus.Completed)
                {
                    skipped++;
                    continue; // Content unchanged — skip
                }
            }

            // Re-ingest the entity
            await IngestEntity<TEntity>(entity, effectiveMetadata, ct);
            processed++;
        }

        // Persist graph after rebuild
        await _graphStore.Save(ct);

        _logger.LogInformation(
            "Rebuild complete: {Processed} processed, {Skipped} skipped (unchanged)",
            processed, skipped);
    }

    public Task<RagCorpusStats> GetStats<TEntity>(
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        var graphStats = _graphStore.GetStats();
        var treeStats = _treeStore.GetStats();
        return Task.FromResult(new RagCorpusStats
        {
            Entities = graphStats.EntityCount,
            Relationships = graphStats.RelationshipCount,
            FreshnessScore = graphStats.LastPersisted.HasValue
                ? Math.Max(0, 1.0 - (DateTimeOffset.UtcNow - graphStats.LastPersisted.Value).TotalDays / 30.0)
                : 0.0,
            LastFullReindex = graphStats.LastPersisted,
            TreeNodes = treeStats.TotalNodes,
            TreeDepth = treeStats.TreeDepth,
            TreeLastBuildTime = treeStats.LastBuildTime
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
        await _graphStore.Clear(ct);

        // Clear distillation tree
        await _treeStore.Clear(ct);
    }

    // ── Internal Pipeline Steps ─────────────────────────────────────────

    private async Task<(int ChunksCreated, int EntitiesExtracted)> IngestSingleFile<TEntity>(
        string filePath,
        RagCorpusMetadata metadata,
        CancellationToken ct) where TEntity : class, IEntity<string>
    {
        _logger.LogDebug("Ingesting file {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);

        // Large file path: segment first, then process each segment independently
        if (_segmenter.CanSegment(filePath, fileInfo.Length))
        {
            var totalChunks = 0;
            var totalEntities = 0;
            var leafEmbeddings = new List<EmbeddingWithText>();
            var documentId = ComputeDocumentId(filePath);
            var documentTitle = Path.GetFileName(filePath);

            _logger.LogDebug("Segmenting large file '{File}'", filePath);

            await foreach (var segment in _segmenter.Segment(filePath, ct))
            {
                ct.ThrowIfCancellationRequested();

                var segText = System.Text.Encoding.UTF8.GetString(segment.Bytes);
                if (string.IsNullOrWhiteSpace(segText)) continue;

                var segTitle = segment.SectionTitle ?? documentTitle;
                var chunked = await _chunker.Chunk(segText, segTitle, metadata.Directive, ct);

                foreach (var child in chunked.ChildChunks)
                {
                    ct.ThrowIfCancellationRequested();
                    var embedding = await Koan.AI.Client.Embed(child.Text, ct);
                    var chunkId = $"{documentId}:{segment.SegmentId}:{child.Id}";

                    await Koan.Data.Vector.Vector<TEntity>.Save(
                        chunkId,
                        embedding,
                        new Dictionary<string, object>
                        {
                            ["document_id"] = documentId,
                            ["parent_id"] = child.ParentId ?? "",
                            ["section"] = child.SectionTitle ?? segment.StructuralContext ?? "",
                            ["title"] = documentTitle ?? "",
                            ["is_child"] = true
                        },
                        ct);

                    leafEmbeddings.Add(new EmbeddingWithText(chunkId, embedding, child.Text));
                    totalChunks++;
                }

                if (metadata.GraphStrategy != GraphStrategy.Lazy)
                {
                    totalEntities += await ExtractAndResolveEntities(
                        chunked.ParentChunks, documentId, segTitle, metadata, ct);
                }
            }

            if (leafEmbeddings.Count >= 3)
                await _treeBuilder.BuildTree(leafEmbeddings, metadata.Directive, ct);

            return (totalChunks, totalEntities);
        }

        // Standard path for small/non-text files
        var extractionResult = await _contentRegistry.ExtractFromFile(
            filePath, metadata.Directive, ct);

        if (string.IsNullOrWhiteSpace(extractionResult.Text))
            return (0, 0);

        var docId = ComputeDocumentId(filePath);
        var docTitle = Path.GetFileName(filePath);

        _logger.LogDebug(
            "File '{File}' extracted via strategy '{Strategy}' ({Rounds} rounds)",
            filePath, extractionResult.StrategyUsed, extractionResult.RoundsExecuted);

        // Contextual chunking of the extracted text
        var chunkedDoc = await _chunker.Chunk(
            extractionResult.Text, docTitle, metadata.Directive, ct);

        // Embed child chunks and store with parent-child metadata
        var chunksCreated = 0;
        var docLeafEmbeddings = new List<EmbeddingWithText>();
        foreach (var child in chunkedDoc.ChildChunks)
        {
            ct.ThrowIfCancellationRequested();

            var embedding = await Koan.AI.Client.Embed(child.Text, ct);
            var chunkId = $"{docId}:{child.Id}";

            await Koan.Data.Vector.Vector<TEntity>.Save(
                chunkId,
                embedding,
                new Dictionary<string, object>
                {
                    ["document_id"] = docId,
                    ["parent_id"] = child.ParentId ?? "",
                    ["section"] = child.SectionTitle ?? "",
                    ["title"] = docTitle ?? "",
                    ["is_child"] = true
                },
                ct);

            docLeafEmbeddings.Add(new EmbeddingWithText(chunkId, embedding, child.Text));
            chunksCreated++;
        }

        // Extract entities from parent chunks
        var entitiesExtracted = 0;
        if (metadata.GraphStrategy != GraphStrategy.Lazy)
        {
            entitiesExtracted = await ExtractAndResolveEntities(
                chunkedDoc.ParentChunks, docId, docTitle, metadata, ct);
        }

        // Build per-document distillation tree if there are enough chunks
        if (docLeafEmbeddings.Count >= 3)
            await _treeBuilder.BuildTree(docLeafEmbeddings, metadata.Directive, ct);

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

    private static string ComputeDocumentId(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized));
        var shortHash = Convert.ToHexStringLower(hash[..8]);
        return $"{Path.GetFileNameWithoutExtension(filePath)}-{shortHash}";
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> _titlePropertyCache = new();

    private static string? ExtractTitle<TEntity>(TEntity entity) where TEntity : class
    {
        var prop = _titlePropertyCache.GetOrAdd(typeof(TEntity), static type =>
            type.GetProperty("Title")
            ?? type.GetProperty("Name")
            ?? type.GetProperty("Subject"));

        return prop?.GetValue(entity)?.ToString();
    }
}
