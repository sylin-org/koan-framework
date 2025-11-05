using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Services.Workers;

/// <summary>
/// Background worker that processes media items in "vectorization-queue" partition,
/// generates/retrieves embeddings via signature-based cache, and moves completed
/// items to default (live) partition.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class VectorizationWorker : BackgroundService
{
    private readonly ILogger<VectorizationWorker> _logger;
    private const string ModelId = "nomic-embed-text"; // TODO: Make configurable
    private const int MaxRetries = 3;

    public VectorizationWorker(ILogger<VectorizationWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VectorizationWorker started (model: {ModelId})", ModelId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll "vectorization-queue" partition for media items
                List<Media> batch;
                using (EntityContext.Partition("vectorization-queue"))
                {
                    var result = await Media.Query(
                        m => m.ContentSignature != null && m.RetryCount < MaxRetries,
                        new DataQueryOptions { PageSize = 10 },
                        stoppingToken);
                    batch = result.ToList();
                }

                if (batch.Any())
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                }
                else
                {
                    // No items to vectorize - wait before polling again
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VectorizationWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("VectorizationWorker stopped");
    }

    private async Task ProcessBatchAsync(List<Media> batch, CancellationToken ct)
    {
        // ───────────────────────────────────────────────────────────
        // Batch fetch embedding cache entries for efficiency
        // ───────────────────────────────────────────────────────────
        var cacheIds = batch
            .Select(m => EmbeddingCacheEntry.MakeCacheId(m.ContentSignature!, ModelId, "Media"))
            .ToList();

        var cachedEmbeddings = await EmbeddingCacheEntry.Get(cacheIds, ct);

        // Create lookup map
        var cacheMap = cacheIds
            .Zip(cachedEmbeddings)
            .Where(pair => pair.Second != null)
            .ToDictionary(pair => pair.First, pair => pair.Second!);

        _logger.LogDebug(
            "Processing batch of {Count} media: {CacheHits} cache hits, {CacheMisses} cache misses",
            batch.Count, cacheMap.Count, batch.Count - cacheMap.Count);

        // Process each media item
        foreach (var media in batch)
        {
            try
            {
                var cacheId = EmbeddingCacheEntry.MakeCacheId(
                    media.ContentSignature!, ModelId, "Media");

                float[] embedding;
                bool cacheHit = false;

                if (cacheMap.TryGetValue(cacheId, out var cached))
                {
                    // ✅ CACHE HIT: Reuse existing embedding
                    embedding = cached.Embedding;
                    cacheHit = true;

                    // Update access tracking
                    cached.RecordAccess();
                    await cached.Save(ct);

                    _logger.LogInformation(
                        "Cache hit for {MediaId} (sig: {Sig}, accessed {Count} times)",
                        media.Id, media.ContentSignature![..8], cached.AccessCount);
                }
                else
                {
                    // ❌ CACHE MISS: Generate new embedding
                    var text = EmbeddingUtilities.BuildEmbeddingText(media);

                    // Check if AI is available
                    var ai = Ai.TryResolve();
                    if (ai == null)
                    {
                        throw new InvalidOperationException(
                            "AI service not available - cannot generate embeddings");
                    }

                    // Generate embedding using Ai service
                    embedding = await Ai.Embed(text, ct);

                    // Store in entity-based cache
                    var entry = new EmbeddingCacheEntry
                    {
                        ContentSignature = media.ContentSignature!,
                        ModelId = ModelId,
                        EntityType = "Media",
                        Embedding = embedding,
                        Dimension = embedding.Length
                    };

                    // Set ID via lifecycle hook or manually
                    entry.Id = EmbeddingCacheEntry.MakeCacheId(
                        entry.ContentSignature, entry.ModelId, entry.EntityType);

                    await entry.Save(ct);

                    _logger.LogInformation(
                        "Generated embedding for {MediaId} (sig: {Sig}, dim: {Dim})",
                        media.Id, media.ContentSignature![..8], embedding.Length);
                }

                // Save vector to Weaviate (in vectorization-queue partition context)
                using (EntityContext.Partition("vectorization-queue"))
                {
                    if (Vector<Media>.IsAvailable)
                    {
                        var vectorMetadata = new Dictionary<string, object>
                        {
                            ["title"] = media.Title ?? "",
                            ["genres"] = media.Genres ?? Array.Empty<string>(),
                            ["popularity"] = media.Popularity
                        };
                        await VectorData<Media>.SaveWithVector(media, embedding, vectorMetadata, ct);
                    }
                    else
                    {
                        _logger.LogWarning("Vector store not available, skipping vector save for {MediaId}", media.Id);
                    }
                }

                // Update media metadata
                media.VectorizedAt = DateTimeOffset.UtcNow;
                media.ProcessingError = null; // Clear any previous errors
                media.RetryCount = 0; // Reset retry count on success

                using (EntityContext.Partition("vectorization-queue"))
                {
                    await media.Save(ct);
                }

                // ───────────────────────────────────────────────────────
                // Move to default (live) partition
                // ───────────────────────────────────────────────────────
                await Media.Copy(m => m.Id == media.Id)
                    .From(partition: "vectorization-queue")
                    .To(partition: null) // default = live
                    .Run(ct);

                using (EntityContext.Partition("vectorization-queue"))
                {
                    await Media.Remove(media.Id!, ct);
                }

                _logger.LogInformation(
                    "Vectorized {MediaId} ({CacheStatus}), moved to live partition",
                    media.Id, cacheHit ? "cache hit" : "generated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to vectorize {MediaId} (attempt {Retry}/{MaxRetries}): {Error}",
                    media.Id, media.RetryCount + 1, MaxRetries, ex.Message);

                media.ProcessingError = ex.Message;
                media.RetryCount++;

                if (media.RetryCount >= MaxRetries)
                {
                    _logger.LogError(
                        "Media {MediaId} exceeded retry limit ({MaxRetries}), discarding",
                        media.Id, MaxRetries);

                    // Remove from queue after max retries
                    using (EntityContext.Partition("vectorization-queue"))
                    {
                        await Media.Remove(media.Id!, ct);
                    }
                }
                else
                {
                    // Save error state and retry later
                    using (EntityContext.Partition("vectorization-queue"))
                    {
                        await media.Save(ct);
                    }
                }
            }
        }
    }
}
