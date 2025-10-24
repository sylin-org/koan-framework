using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IPassageIndexer
{
    Task IndexAsync(string pipelineId, List<Passage> passages, CancellationToken ct);
}

public sealed class PassageIndexer : IPassageIndexer
{
    private readonly ILogger<PassageIndexer> _logger;
    private readonly IPipelineAlertService _alerts;
    private readonly IEmbeddingCache _cache;
    private const string EmbeddingModel = "granite3.3:8b";

    public PassageIndexer(
        ILogger<PassageIndexer> logger,
        IPipelineAlertService alerts,
        IEmbeddingCache cache)
    {
        _logger = logger;
        _alerts = alerts;
        _cache = cache;
    }

    public async Task IndexAsync(string pipelineId, List<Passage> passages, CancellationToken ct)
    {
        if (passages.Count == 0)
        {
            return;
        }

        if (!VectorWorkflow<Passage>.IsAvailable(MeridianConstants.VectorProfile))
        {
            _logger.LogWarning("Vector workflow {Profile} unavailable; skipping indexing.", MeridianConstants.VectorProfile);
            await _alerts.PublishWarning(pipelineId, "vector-unavailable", $"Vector profile '{MeridianConstants.VectorProfile}' unavailable; retrieval falls back to lexical search.", ct);
            return;
        }

        await VectorWorkflow<Passage>.EnsureCreated(MeridianConstants.VectorProfile, ct);

        var hits = 0;
        var misses = 0;
        var payload = new List<(Passage Entity, float[] Embedding, object? Metadata)>();

        foreach (var passage in passages)
        {
            // Check cache
            var contentHash = EmbeddingCache.ComputeContentHash(passage.Text);
            var cached = await _cache.GetAsync(contentHash, EmbeddingModel, nameof(Passage), ct);

            float[] embedding;
            if (cached != null)
            {
                hits++;
                _logger.LogDebug("Embedding cache HIT for passage {PassageId}", passage.Id);
                embedding = cached.Embedding;
            }
            else
            {
                misses++;
                _logger.LogDebug("Embedding cache MISS for passage {PassageId}", passage.Id);
                embedding = await Koan.AI.Ai.Embed(passage.Text, ct);
                await _cache.SetAsync(contentHash, EmbeddingModel, embedding, nameof(Passage), ct);
            }

            payload.Add((passage, embedding, BuildMetadata(passage)));
            passage.IndexedAt = DateTime.UtcNow;
            await passage.Save(ct);
        }

        _logger.LogInformation("Embedding cache: {Hits} hits, {Misses} misses ({Total} total)",
            hits, misses, passages.Count);

        if (payload.Count > 0)
        {
            var result = await VectorWorkflow<Passage>.SaveMany(payload, MeridianConstants.VectorProfile, ct);
            _logger.LogInformation("Upserted {Count} passages into vector profile {Profile}.", result.Documents, MeridianConstants.VectorProfile);
        }
    }

    private static Dictionary<string, object?> BuildMetadata(Passage passage)
        => new()
        {
            ["docId"] = passage.Id, // Required for VectorWorkflow to map Weaviate results back to entity IDs
            ["sourceDocumentId"] = passage.SourceDocumentId,
            ["sequenceNumber"] = passage.SequenceNumber,
            ["section"] = passage.Section
        };
}
