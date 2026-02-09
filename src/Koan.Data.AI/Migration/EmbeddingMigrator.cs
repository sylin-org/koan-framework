using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Logging;

namespace Koan.Data.AI.Migration;

/// <summary>
/// Utility for migrating embeddings between providers, models, or vector databases.
/// Part of ADR AI-0020: Entity-First AI Integration and Transaction Coordination (Phase 4).
/// </summary>
/// <remarks>
/// Supports:
/// - Re-embedding entities with a different model/provider
/// - Migrating between vector databases (copy vectors)
/// - Batch re-embedding after schema version upgrades
/// - Export/import for backup and disaster recovery
///
/// Usage:
/// <code>
/// // Re-embed all Media entities with a new model
/// await EmbeddingMigrator.ReEmbedAll&lt;Media&gt;(
///     targetModel: "text-embedding-3-large",
///     targetSource: "openai-prod",
///     batchSize: 50,
///     logger: logger);
///
/// // Migrate embeddings after version upgrade
/// await EmbeddingMigrator.MigrateToVersion&lt;Media&gt;(
///     newVersion: 2,
///     batchSize: 100,
///     logger: logger);
/// </code>
/// </remarks>
public static class EmbeddingMigrator
{
    /// <summary>
    /// Re-embeds all entities of a given type with a new model/provider.
    /// Useful for switching from local Ollama to cloud OpenAI, or upgrading embedding models.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="targetModel">Target embedding model (e.g., "text-embedding-3-large")</param>
    /// <param name="targetSource">Target AI source/group (e.g., "openai-prod")</param>
    /// <param name="targetProvider">Target AI provider (e.g., "openai", "ollama")</param>
    /// <param name="batchSize">Number of entities to process per batch</param>
    /// <param name="parallel">Whether to process batches in parallel (default: false)</param>
    /// <param name="logger">Logger for progress reporting</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Migration statistics</returns>
    public static async Task<MigrationResult> ReEmbedAll<TEntity>(
        string? targetModel = null,
        string? targetSource = null,
        string? targetProvider = null,
        int batchSize = 50,
        bool parallel = false,
        ILogger? logger = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();
        logger?.LogInformation(
            "Starting re-embedding migration for {EntityType}: model={Model}, source={Source}, provider={Provider}",
            typeof(TEntity).Name, targetModel ?? "default", targetSource ?? "default", targetProvider ?? "default");

        var result = new MigrationResult
        {
            EntityType = typeof(TEntity).Name,
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Load all entities
            var entities = (await Data<TEntity, string>.Query(e => true, ct)).ToList();
            result.TotalEntities = entities.Count;

            logger?.LogInformation("Loaded {Count} entities for re-embedding", entities.Count);

            // Process in batches
            var batches = entities.Chunk(batchSize).ToList();
            result.TotalBatches = batches.Count;

            if (parallel)
            {
                await Parallel.ForEachAsync(batches, ct, async (batch, token) =>
                {
                    await ProcessBatch(batch, metadata, targetModel, targetSource, targetProvider, result, logger, token);
                });
            }
            else
            {
                foreach (var batch in batches)
                {
                    await ProcessBatch(batch, metadata, targetModel, targetSource, targetProvider, result, logger, ct);
                    result.ProcessedBatches++;

                    logger?.LogInformation(
                        "Batch {Current}/{Total} complete ({Percent}%)",
                        result.ProcessedBatches, result.TotalBatches,
                        (result.ProcessedBatches * 100.0 / result.TotalBatches).ToString("F1"));
                }
            }

            result.CompletedAt = DateTimeOffset.UtcNow;
            result.Success = true;

            logger?.LogInformation(
                "Migration completed: {Success}/{Total} entities re-embedded in {Duration}",
                result.SuccessfulEntities, result.TotalEntities, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Migration failed: {Error}", ex.Message);
            result.CompletedAt = DateTimeOffset.UtcNow;
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Migrates embeddings to a new schema version.
    /// Forces re-embedding of all entities when [Embedding(Version = X)] is incremented.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="newVersion">New version number (should match updated [Embedding] attribute)</param>
    /// <param name="batchSize">Number of entities to process per batch</param>
    /// <param name="parallel">Whether to process batches in parallel</param>
    /// <param name="logger">Logger for progress reporting</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Migration statistics</returns>
    public static async Task<MigrationResult> MigrateToVersion<TEntity>(
        int newVersion,
        int batchSize = 100,
        bool parallel = false,
        ILogger? logger = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        logger?.LogInformation(
            "Starting version migration for {EntityType}: version={NewVersion}",
            typeof(TEntity).Name, newVersion);

        // Version migration is just a re-embed with version-aware signatures
        // The EmbeddingMetadata.ComputeSignature already includes version in signature
        return await ReEmbedAll<TEntity>(
            batchSize: batchSize,
            parallel: parallel,
            logger: logger,
            ct: ct);
    }

    /// <summary>
    /// Exports embeddings to a portable format for backup or migration.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="outputPath">Path to output JSON file</param>
    /// <param name="logger">Logger for progress reporting</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task ExportEmbeddings<TEntity>(
        string outputPath,
        ILogger? logger = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        logger?.LogInformation("Exporting embeddings for {EntityType} to {Path}", typeof(TEntity).Name, outputPath);

        // Load all embedding states
        var states = await EmbeddingState<TEntity>.Query(s => true, ct);
        var statesList = states.ToList();

        logger?.LogInformation("Loaded {Count} embedding states", statesList.Count);

        // Export to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(statesList, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(outputPath, json, ct);

        logger?.LogInformation("Export completed: {Count} embeddings saved to {Path}", statesList.Count, outputPath);
    }

    /// <summary>
    /// Cleans up orphaned embedding states (entities that no longer exist).
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="logger">Logger for progress reporting</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of orphaned states removed</returns>
    public static async Task<int> CleanupOrphanedStates<TEntity>(
        ILogger? logger = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        logger?.LogInformation("Cleaning up orphaned embedding states for {EntityType}", typeof(TEntity).Name);

        var removedCount = 0;
        var states = await EmbeddingState<TEntity>.Query(s => true, ct);

        foreach (var state in states)
        {
            // Check if entity still exists
            var entity = await Data<TEntity, string>.GetAsync(state.EntityId, ct);
            if (entity == null)
            {
                // Entity deleted - remove orphaned state
                await EmbeddingState<TEntity>.Remove(state.Id!, ct);
                removedCount++;
            }
        }

        logger?.LogInformation("Cleanup completed: {Count} orphaned states removed", removedCount);
        return removedCount;
    }

    private static async Task ProcessBatch<TEntity>(
        IEnumerable<TEntity> batch,
        EmbeddingMetadata metadata,
        string? targetModel,
        string? targetSource,
        string? targetProvider,
        MigrationResult result,
        ILogger? logger,
        CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        foreach (var entity in batch)
        {
            try
            {
                // Build embedding text
                var text = metadata.BuildEmbeddingText(entity);
                var signature = metadata.ComputeSignature(entity);

                // Generate embedding with target model/source
                float[] embedding;
                using (targetSource != null || targetProvider != null || targetModel != null
                    ? Client.Scope(all: targetSource)
                    : null)
                {
                    embedding = await Client.Embed(text, ct);
                }

                // Save to vector database
                await VectorData<TEntity>.SaveWithVector(entity, embedding, null, ct);

                // Update embedding state
                var stateId = EmbeddingState<TEntity>.MakeId(entity.Id);
                var state = await EmbeddingState<TEntity>.Get(stateId, ct);

                if (state == null)
                {
                    state = new EmbeddingState<TEntity>
                    {
                        Id = stateId,
                        EntityId = entity.Id,
                        ContentSignature = signature,
                        LastEmbeddedAt = DateTimeOffset.UtcNow,
                        Model = targetModel ?? metadata.Model
                    };
                }
                else
                {
                    state.ContentSignature = signature;
                    state.LastEmbeddedAt = DateTimeOffset.UtcNow;
                    state.Model = targetModel ?? metadata.Model;
                }

                await state.Save(ct);

                Interlocked.Increment(ref result.SuccessfulEntities);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref result.FailedEntities);
                logger?.LogError(ex, "Failed to re-embed entity {EntityId}: {Error}", entity.Id, ex.Message);
            }
        }
    }
}

/// <summary>
/// Result of an embedding migration operation.
/// </summary>
public sealed class MigrationResult
{
    public string EntityType { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TotalEntities { get; set; }
    public int SuccessfulEntities;
    public int FailedEntities;
    public int TotalBatches { get; set; }
    public int ProcessedBatches;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public TimeSpan Duration => (CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt;
    public double SuccessRate => TotalEntities > 0 ? (SuccessfulEntities / (double)TotalEntities) * 100.0 : 0.0;
}
