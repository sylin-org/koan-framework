using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Selection;
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
    /// <param name="targetProvider">Optional provider provenance label. Routing is selected by <paramref name="targetSource"/>.</param>
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
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var metadata = EmbeddingMetadata.Resolve<TEntity>();
        logger?.LogInformation(
            "Starting re-embedding migration for {EntityType}: model={Model}, source={Source}, provider={Provider}",
            typeof(TEntity).Name, targetModel ?? "default", targetSource ?? "default", targetProvider ?? "default");

        // W4 (AI-0036 P2): re-indexing the whole collection IS a by-design model transition — reset the
        // model registry to the target so the batch writes below don't trip the mixed-space GuardWrite.
        await VectorModelGuard.Reset<TEntity>(targetModel ?? metadata.Model, ct);

        try
        {
            var entities = (await Data<TEntity, string>.Query(e => true, ct)).ToList();
            logger?.LogInformation("Loaded {Count} entities for re-embedding", entities.Count);
            return await ProcessEntities(
                entities, metadata, targetModel, targetSource, targetProvider,
                batchSize, parallel, logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Migration failed: {Error}", ex.Message);
            return Failed<TEntity>(ex);
        }
    }

    /// <summary>
    /// Re-embeds the supplied finite Entity set without loading or changing unrelated entities.
    /// The returned result reports per-operation success and failure counts; no collection atomicity
    /// is implied.
    /// </summary>
    public static async Task<MigrationResult> ReEmbed<TEntity>(
        IEnumerable<TEntity> entities,
        string? targetModel = null,
        string? targetSource = null,
        string? targetProvider = null,
        int batchSize = 50,
        bool parallel = false,
        ILogger? logger = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var metadata = EmbeddingMetadata.Resolve<TEntity>();

        try
        {
            var materialized = entities as IReadOnlyList<TEntity> ?? entities.ToList();
            logger?.LogInformation(
                "Re-embedding {Count} supplied {EntityType} entities: model={Model}, source={Source}, provider={Provider}",
                materialized.Count, typeof(TEntity).Name,
                targetModel ?? metadata.Model ?? "default",
                targetSource ?? metadata.Source ?? "default",
                targetProvider ?? "default");

            return await ProcessEntities(
                materialized, metadata, targetModel, targetSource, targetProvider,
                batchSize, parallel, logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Embedding operation failed: {Error}", ex.Message);
            return Failed<TEntity>(ex);
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

        // Export to JSON via Newtonsoft (framework canon; System.Text.Json's reflection serializer is AOT-disabled)
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(statesList, Newtonsoft.Json.Formatting.Indented);

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
            var entity = await Data<TEntity, string>.Get(state.EntityId, ct);
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

    private static async Task<MigrationResult> ProcessEntities<TEntity>(
        IReadOnlyList<TEntity> entities,
        EmbeddingMetadata metadata,
        string? targetModel,
        string? targetSource,
        string? targetProvider,
        int batchSize,
        bool parallel,
        ILogger? logger,
        CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var result = new MigrationResult
        {
            EntityType = typeof(TEntity).Name,
            StartedAt = DateTimeOffset.UtcNow,
            TotalEntities = entities.Count
        };
        var batches = entities.Chunk(batchSize).ToArray();
        result.TotalBatches = batches.Length;

        if (parallel)
        {
            await Parallel.ForEachAsync(batches, ct, async (batch, token) =>
            {
                await ProcessBatch(
                    batch, metadata, targetModel, targetSource, targetProvider,
                    result, logger, token).ConfigureAwait(false);
                Interlocked.Increment(ref result.ProcessedBatches);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var batch in batches)
            {
                await ProcessBatch(
                    batch, metadata, targetModel, targetSource, targetProvider,
                    result, logger, ct).ConfigureAwait(false);
                result.ProcessedBatches++;

                logger?.LogInformation(
                    "Embedding batch {Current}/{Total} complete ({Percent:F1}%)",
                    result.ProcessedBatches, result.TotalBatches,
                    result.ProcessedBatches * 100.0 / result.TotalBatches);
            }
        }

        result.CompletedAt = DateTimeOffset.UtcNow;
        result.Success = result.FailedEntities == 0;
        logger?.LogInformation(
            "Embedding operation completed: {Success}/{Total} entities indexed in {Duration}",
            result.SuccessfulEntities, result.TotalEntities, result.Duration);
        return result;
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
        await foreach (var entity in EntityCardinality.Many(batch, ct).ConfigureAwait(false))
        {
            try
            {
                var content = EmbeddingWriter.Describe(entity, metadata);
                await EmbeddingWriter.Write(
                    entity,
                    metadata,
                    content,
                    targetModel,
                    targetSource,
                    targetProvider,
                    ct).ConfigureAwait(false);

                Interlocked.Increment(ref result.SuccessfulEntities);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref result.FailedEntities);
                logger?.LogError(ex, "Failed to re-embed entity {EntityId}: {Error}", entity.Id, ex.Message);
            }
        }
    }

    private static MigrationResult Failed<TEntity>(Exception exception)
        => new()
        {
            EntityType = typeof(TEntity).Name,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Success = false,
            ErrorMessage = exception.Message
        };
}

/// <summary>
/// Result of an embedding migration operation.
/// </summary>
public sealed class MigrationResult
{
    public string EntityType { get; init; } = "";
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
