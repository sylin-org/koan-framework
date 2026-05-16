using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IPassageIndexer
{
    Task Index(string pipelineId, List<Passage> passages, CancellationToken ct);
}

public sealed class PassageIndexer : IPassageIndexer
{
    private readonly ILogger<PassageIndexer> _logger;
    private readonly IPipelineAlertService _alerts;

    public PassageIndexer(
        ILogger<PassageIndexer> logger,
        IPipelineAlertService alerts)
    {
        _logger = logger;
        _alerts = alerts;
    }

    public async Task Index(string pipelineId, List<Passage> passages, CancellationToken ct)
    {
        if (passages.Count == 0)
        {
            return;
        }

        // Check vector availability using new Vector<T> API
        if (!Vector<Passage>.IsAvailable)
        {
            _logger.LogWarning("Vector storage unavailable; skipping indexing.");
            await _alerts.PublishWarning(
                pipelineId,
                "vector-unavailable",
                "Vector storage unavailable; retrieval falls back to lexical search.",
                ct);
            return;
        }

        await Vector<Passage>.EnsureCreated(ct);

        var successCount = 0;
        var failureCount = 0;

        // Process passages with transaction coordination for atomic entity + vector saves
        // [Embedding] attribute handles:
        // - Content hash calculation for change detection
        // - Cache lookup via EmbeddingState<Passage>
        // - Embedding generation if content changed
        // - Vector upsert coordination
        foreach (var passage in passages)
        {
            using var tx = EntityContext.Transaction($"passage-{passage.Id}");

            try
            {
                passage.IndexedAt = DateTime.UtcNow;

                // Save triggers [Embedding] attribute lifecycle:
                // 1. Computes content hash from Text property
                // 2. Checks EmbeddingState<Passage> for existing hash
                // 3. Generates embedding if content changed
                // 4. Queues vector upsert for commit
                await passage.Save(ct);

                // Atomic commit: Both entity + vector, or neither
                await EntityContext.Commit(ct);

                successCount++;
                _logger.LogDebug("Indexed passage {PassageId} (transactional)", passage.Id);
            }
            catch (Exception ex)
            {
                failureCount++;

                // Rollback discards both entity and vector operations
                await EntityContext.Rollback(ct);

                _logger.LogError(ex, "Failed to index passage {PassageId}, transaction rolled back", passage.Id);
            }
        }

        _logger.LogInformation(
            "Passage indexing complete: {Success} succeeded, {Failed} failed ({Total} total)",
            successCount,
            failureCount,
            passages.Count);

        if (failureCount > 0)
        {
            await _alerts.PublishWarning(
                pipelineId,
                "indexing-partial-failure",
                $"{failureCount}/{passages.Count} passages failed to index",
                ct);
        }
    }
}
