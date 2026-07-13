using Koan.Data.Core;
using Microsoft.Extensions.Logging;

namespace Koan.Data.AI;

/// <summary>
/// W4 (AI-0036 P2): prevents a vector index from silently becoming a <b>mixed-space</b> — built from
/// more than one embedding model, whose vectors are not comparable. Backed by the durable,
/// per-(entity, partition) <see cref="VectorModelRegistry{TEntity}"/> maintained at write time. Every
/// guarded write reads that host-routed record in O(1); no process cache can bypass backend truth.
/// </summary>
/// <remarks>
/// Posture (AI-0036 §7 decision 3): <b>throw when knowable, warn for by-design multi-model</b>. The
/// guard fires at the genuine boundary — the WRITE that would introduce a second model into a
/// single-model index — and throws <see cref="VectorModelMismatchException"/> there, preventing the
/// corrupt index rather than detecting it after the fact (this also subsumes the read-time
/// query-mismatch: a guarded single-model index never mismatches a same-model query). A legitimate
/// model change re-indexes via the <c>EmbeddingMigrator</c>, which <see cref="Reset"/>s the registry.
/// An index that is already multi-model is tolerated with a WARN. <see cref="Evaluate"/> stays pure
/// for unit testing.
/// </remarks>
public static class VectorModelGuard
{
    private static string PartLabel(string p) => string.IsNullOrEmpty(p) ? "" : $" (partition '{p}')";

    /// <summary>Pure decision used for diagnostics/health: given the index's models and an optional query model.</summary>
    public static VectorModelReport Evaluate(string entity, IReadOnlyList<string> indexModels, string? queryModel)
    {
        var mixedSpace = indexModels.Count > 1;
        var queryMismatch = !string.IsNullOrEmpty(queryModel)
            && indexModels.Count > 0
            && !indexModels.Contains(queryModel);
        return new VectorModelReport(entity, indexModels, mixedSpace, queryMismatch);
    }

    /// <summary>
    /// Pure write-time decision (unit-testable): what to do when writing <paramref name="model"/> into
    /// an index whose recorded models are <paramref name="indexModels"/>.
    /// </summary>
    public static ModelWriteAction DecideWrite(IReadOnlyList<string> indexModels, string model)
    {
        if (indexModels.Count == 0) return ModelWriteAction.Record;          // first model -> establish
        if (indexModels.Contains(model)) return ModelWriteAction.AlreadyPresent;
        if (indexModels.Count == 1) return ModelWriteAction.Throw;           // 2nd model into single-model index
        return ModelWriteAction.WarnAndRecord;                              // already multi-model -> tolerate
    }

    /// <summary>
    /// Write-time guard: records <paramref name="model"/> as a producer of the current (entity,
    /// partition) vector index, and THROWS <see cref="VectorModelMismatchException"/> if it would
    /// introduce a second model into a single-model index. Call immediately before writing a vector.
    /// Unknown (null/empty) model is a no-op (cannot guard). Performs one O(1) registry read per
    /// guarded write so the decision follows the current host and backend.
    /// </summary>
    public static async Task GuardWrite<TEntity>(string? model, ILogger? logger = null, CancellationToken ct = default)
        where TEntity : class
    {
        if (string.IsNullOrEmpty(model)) return;
        var entity = typeof(TEntity).Name;
        var partition = EntityContext.Current?.Partition ?? string.Empty;

        var id = VectorModelRegistry<TEntity>.MakeId(partition);
        var reg = await VectorModelRegistry<TEntity>.Get(id, ct);
        var current = reg?.Models ?? (IReadOnlyList<string>)Array.Empty<string>();

        switch (DecideWrite(current, model))
        {
            case ModelWriteAction.AlreadyPresent:
                break;
            case ModelWriteAction.Throw:
                // single-model index + a different model = accidental mixed-space -> fail loud at the boundary
                throw new VectorModelMismatchException(entity, partition, current[0], model);
            case ModelWriteAction.WarnAndRecord:
                logger?.LogWarning(
                    "Vector index for {Entity}{Part} is already multi-model ({Models}); recording '{Model}'. " +
                    "Mixed-space indexes return unreliable neighbours — re-index via EmbeddingMigrator.",
                    entity, PartLabel(partition), string.Join(", ", current), model);
                goto case ModelWriteAction.Record;
            case ModelWriteAction.Record:
                reg ??= new VectorModelRegistry<TEntity> { Id = id, Partition = partition };
                reg.Models.Add(model);
                await reg.Save(ct);
                break;
        }
    }

    /// <summary>
    /// Resets the (entity, partition) registry to a single model — used by the EmbeddingMigrator when
    /// re-indexing the whole collection to a new model (a by-design model transition), so subsequent
    /// writes of the target model do not trip <see cref="GuardWrite{TEntity}"/>.
    /// </summary>
    public static async Task Reset<TEntity>(string? model, CancellationToken ct = default)
        where TEntity : class
    {
        var partition = EntityContext.Current?.Partition ?? string.Empty;
        var id = VectorModelRegistry<TEntity>.MakeId(partition);
        var reg = await VectorModelRegistry<TEntity>.Get(id, ct)
                  ?? new VectorModelRegistry<TEntity> { Id = id, Partition = partition };
        reg.Models = string.IsNullOrEmpty(model) ? new List<string>() : new List<string> { model! };
        await reg.Save(ct);
    }

    /// <summary>The distinct producing models for the current (entity, partition) index — O(1) registry read.</summary>
    public static async Task<IReadOnlyList<string>> ModelsInIndex<TEntity>(CancellationToken ct = default)
        where TEntity : class
    {
        var partition = EntityContext.Current?.Partition ?? string.Empty;
        var reg = await VectorModelRegistry<TEntity>.Get(VectorModelRegistry<TEntity>.MakeId(partition), ct);
        return reg?.Models ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// Diagnostics/health self-report: reads the registry and WARNS on a mixed-space index or a
    /// query-model mismatch (never throws — the hard throw is at write time). Surfaces "models in index".
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

/// <summary>The write-time decision for <see cref="VectorModelGuard.DecideWrite"/>.</summary>
public enum ModelWriteAction
{
    /// <summary>First/new model on a fresh or multi-model index — record it.</summary>
    Record,
    /// <summary>Model already recorded — no-op.</summary>
    AlreadyPresent,
    /// <summary>A second model into a single-model index — fail loud (would create a mixed-space index).</summary>
    Throw,
    /// <summary>Index already multi-model — record with a warning (do not block).</summary>
    WarnAndRecord
}
