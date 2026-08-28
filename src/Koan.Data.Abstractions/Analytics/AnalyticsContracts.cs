using Koan.Data.Abstractions.Filtering;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Abstractions.Analytics;

public enum AnalyticsMeasureKind
{
    Count,
    Sum,
    Min,
    Max,
    Average
}

/// <summary>How an answer reports that its row cap interacted with the result.</summary>
public enum AnalyticsCompletion
{
    /// <summary>Every matching group/value is present.</summary>
    Complete,
    /// <summary>The answer reached the declared row cap; more groups exist than are shown.</summary>
    RowCapped
}

/// <summary>One answer row — the group value and the measure, keyed by their catalog names.</summary>
public sealed class AnalyticsRow
{
    public required IReadOnlyDictionary<string, object?> Values { get; init; }
}

/// <summary>
/// The answer plus everything true about how it was produced. The envelope is not decoration: silent
/// staleness and silent capping are the two documented trust-killers of this feature class, so the
/// engine, the age, and the bounds are part of the answer itself (DATA-0123).
/// </summary>
public sealed class AnalyticsResult
{
    public required string Question { get; init; }
    public required string Engine { get; init; }

    /// <summary>
    /// How old the answer was when it left the store — <c>live</c> for on-demand asks; a duration label
    /// once materialized answers exist (ANL-3).
    /// </summary>
    public required string Age { get; init; }

    /// <summary>Which path produced the answer — <c>materialization</c> or <c>live</c>. The serve-or-compute
    /// decision is visible on every answer, never silent (the documented #1 trust-killer is invisible staleness).</summary>
    public required string ServedFrom { get; init; }

    public required int RowCap { get; init; }
    public required AnalyticsCompletion Completion { get; init; }
    public required IReadOnlyList<AnalyticsRow> Rows { get; init; }
}

/// <summary>
/// The catalog-facing face of a declared question. A question is immutable after declaration: it is the
/// unit of analytics, and everything the answer needs to say about itself (bounds, engine, age) was
/// decided at declaration time, not at the call site. Composers — which live in the connector that owns
/// the entity's mapping — read the same public shape the catalog publishes.
/// </summary>
public abstract class AnalyticsQuestion
{
    protected AnalyticsQuestion(
        string name,
        Type entityType,
        int rowCap,
        Filter? filter,
        string? groupMember,
        AnalyticsMeasureKind measureKind,
        string? measureMember,
        AnalyticsProjectionPolicy? projection,
        IReadOnlyList<AnalyticsProjectionColumn> columns)
    {
        Name = name;
        EntityType = entityType;
        RowCap = rowCap;
        Filter = filter;
        GroupMember = groupMember;
        MeasureKind = measureKind;
        MeasureMember = measureMember;
        Projection = projection;
        Columns = columns;
    }

    public string Name { get; }
    public Type EntityType { get; }
    public int RowCap { get; }

    /// <summary>The optional predicate the answer respects, in the framework's filter model.</summary>
    public Filter? Filter { get; }

    /// <summary>The member answers are grouped by, when the question groups.</summary>
    public string? GroupMember { get; }

    public AnalyticsMeasureKind MeasureKind { get; }

    /// <summary>The member the measure aggregates, when one exists.</summary>
    public string? MeasureMember { get; }

    /// <summary>
    /// The projection policy when this question materializes; null for on-demand questions. Only
    /// materialized questions are answerable through the read-model door.
    /// </summary>
    public AnalyticsProjectionPolicy? Projection { get; }

    /// <summary>The materialization's columns, in declaration order (group first, then the measure).</summary>
    public IReadOnlyList<AnalyticsProjectionColumn> Columns { get; }

    /// <summary>Run the question — the closed-generic door captured at declaration.</summary>
    public abstract Task<AnalyticsResult> ExecuteAsync(IServiceProvider services, int rowCap, CancellationToken cancellationToken);

    /// <summary>
    /// Re-materialize a projection: compute over the record store, replace the engine's rows, stamp the
    /// refresh state. On-demand questions refuse — there is nothing to refresh.
    /// </summary>
    public abstract Task<ProjectionRefreshReceipt> RefreshAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>What one projection refresh did — the receipt for triggers, loops, and facts.</summary>
public sealed record ProjectionRefreshReceipt(string Recipe, int RowCount, DateTimeOffset RefreshedUtc);

/// <summary>A page of materialized rows — the read-model door's answer.</summary>
public sealed class AnalyticsReadModelResult
{
    public required string Question { get; init; }
    public required AnalyticsCompletion Completion { get; init; }
    public required IReadOnlyList<AnalyticsRow> Rows { get; init; }
}

/// <summary>
/// The connector-side half of the analytics grammar: the repository that owns an entity's physical
/// mapping and dialect composes its own SQL for a declared question. The framework owns the question,
/// the bounds, and the envelope; the adapter owns the words (DATA-0119's split, applied to analytics).
/// Implemented by relational repositories and forwarded through the repository facade.
/// </summary>
public interface IAnalyticsQueryComposer<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Compose the bounded ask. <paramref name="corrective"/> explains the refusal when the question —
    /// or the store it would run against — cannot answer it honestly.
    /// </summary>
    bool TryCompose(AnalyticsQuestion question, out AnalyticsSql sql, out string? corrective);
}

/// <summary>
/// Optional engine capability: export a materialization as Parquet. Engines whose storage can emit
/// Parquet server-side (DuckDB via COPY) implement this; the read-model door advertises the format
/// only when the sink does, so the export is never a silent empty file.
/// </summary>
public interface IAnalyticsParquetExport
{
    Task<byte[]> ExportParquetAsync(
        string recipe,
        IReadOnlyDictionary<string, object?>? equalityFilters,
        CancellationToken cancellationToken);
}

/// <summary>One composed ask: the SQL text, its named parameters, and the output column order.</summary>
public sealed record AnalyticsSql(
    string Text,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<string> OutputNames,
    string Provider);

/// <summary>The declared refresh/serving policy of a materialized projection.</summary>
public sealed record AnalyticsProjectionPolicy
{
    /// <summary>Scheduled refresh cadence. Null means refresh is manual/trigger-only.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>
    /// Declared freshness tolerance: a materialization no older than this is served instead of computed.
    /// Null means the materialization is always served when it exists.
    /// </summary>
    public TimeSpan? ServeWithin { get; init; }

    /// <summary>When serving found the answer stale, re-materialize as part of answering.</summary>
    public bool BackfillOnRead { get; init; }
}

/// <summary>One column of a materialization, as it is written into and read from the engine.</summary>
public sealed record AnalyticsProjectionColumn(string Name, Type ClrType);

/// <summary>What the engine knows about one materialization's last refresh.</summary>
public sealed class ProjectionMaterializationState
{
    public required string Recipe { get; init; }
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public int? RowCount { get; init; }
    public long? DurationMs { get; init; }
}

/// <summary>
/// The elected engine's materialization surface: where declared projections are stored, refreshed, and
/// read back. Implemented by the engine connector (DuckDB first — the per-host derived store is exactly
/// what its single-writer model wants).
/// </summary>
public interface IAnalyticsProjectionSink
{
    /// <summary>Refresh bookkeeping for one projection — what facts and the serve-or-compute decision read.</summary>
    Task<ProjectionMaterializationState?> ReadStateAsync(string recipe, CancellationToken cancellationToken);

    /// <summary>Create the materialization table when absent.</summary>
    Task EnsureAsync(string recipe, IReadOnlyList<AnalyticsProjectionColumn> columns, CancellationToken cancellationToken);

    /// <summary>Replace the materialization's rows with <paramref name="rows"/> and stamp the refresh state.</summary>
    Task WriteRowsAsync(
        string recipe,
        IReadOnlyList<AnalyticsProjectionColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        DateTimeOffset refreshUtc,
        long durationMs,
        CancellationToken cancellationToken);

    /// <summary>Read materialized rows, optionally equality-filtered on declared columns.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        string recipe,
        int limit,
        int offset,
        IReadOnlyDictionary<string, object?>? equalityFilters,
        CancellationToken cancellationToken);
}
