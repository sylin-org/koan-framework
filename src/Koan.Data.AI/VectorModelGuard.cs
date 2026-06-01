using Microsoft.Extensions.Logging;

namespace Koan.Data.AI;

/// <summary>
/// AI-0036 P2 (W4): detects when a vector index is a <b>mixed-space</b> — built from more than one
/// embedding model — or when a query embedding's model is absent from the index's model set. Vectors
/// produced by different models are not comparable, so similarity results across a mixed-space index
/// are silently wrong; this surfaces that hazard.
/// </summary>
/// <remarks>
/// Posture is <b>WARN-only</b> (AI-0036 §7 decision 3): it logs + reports, never throws. The hard
/// throw is deferred until a durable, O(1), never-stale write-time per-collection model registry backs
/// it — without that, a throw would risk false positives (blocking a legitimate query) and false
/// negatives. The model set is read from the persisted <see cref="EmbeddingState{TEntity}"/> (the
/// lifecycle owner already records the producing model per entity), so there is no vector-store scan
/// and no new infrastructure. <see cref="Evaluate"/> is pure so the decision logic is unit-testable.
/// </remarks>
public static class VectorModelGuard
{
    /// <summary>Pure decision: given the models that produced the index and an optional query model.</summary>
    public static VectorModelReport Evaluate(string entity, IReadOnlyList<string> indexModels, string? queryModel)
    {
        var mixedSpace = indexModels.Count > 1;
        var queryMismatch = !string.IsNullOrEmpty(queryModel)
            && indexModels.Count > 0
            && !indexModels.Contains(queryModel);
        return new VectorModelReport(entity, indexModels, mixedSpace, queryMismatch);
    }

    /// <summary>The distinct producing models recorded for this entity's index (from EmbeddingState).</summary>
    public static async Task<IReadOnlyList<string>> ModelsInIndex<TEntity>(CancellationToken ct = default)
        where TEntity : class
    {
        var states = await EmbeddingState<TEntity>.All(ct);
        return states
            .Select(s => s.Model)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Inspects the index (and optionally a query model), logging a WARNING on a mixed-space index or
    /// a query-model mismatch, and returns the report for self-reporting. Never throws (warn-only).
    /// Reads the data store, so call it from diagnostics/boot/admin paths — not on every query.
    /// </summary>
    public static async Task<VectorModelReport> Inspect<TEntity>(
        string? queryModel = null, ILogger? logger = null, CancellationToken ct = default)
        where TEntity : class
    {
        var models = await ModelsInIndex<TEntity>(ct);
        var report = Evaluate(typeof(TEntity).Name, models, queryModel);

        if (report.MixedSpace)
            logger?.LogWarning(
                "Vector index for {Entity} is MIXED-SPACE: produced by {Count} embedding models ({Models}). " +
                "Vectors from different models are not comparable; similarity results may be wrong. " +
                "Re-embed to a single model (EmbeddingMigrator) to resolve.",
                report.Entity, models.Count, string.Join(", ", models));

        if (report.QueryMismatch)
            logger?.LogWarning(
                "Query embedding model '{QueryModel}' is not among the models that produced the {Entity} index ({Models}); " +
                "results may be unreliable.",
                queryModel, report.Entity, string.Join(", ", models));

        return report;
    }
}

/// <summary>The W4 inspection result (AI-0036 P2). Surfaced in diagnostics / "models in index" self-report.</summary>
public sealed record VectorModelReport(
    string Entity,
    IReadOnlyList<string> Models,
    bool MixedSpace,
    bool QueryMismatch);
