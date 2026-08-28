using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Analytics;
using Koan.Data.Analytics.Runtime;
using Koan.Core.Hosting.App;

using Koan.Data.Analytics.Recipes;
namespace Koan.Data.Analytics;

/// <summary>
/// The entity-facing analytics surface: declare named questions once, run them anywhere. The call site
/// expresses intent and nothing else — every operational decision (bounds, engine, freshness policy)
/// belongs to the declaration or the composition, and every operational fact travels on the answer
/// (DATA-0123's call-site rule).
/// </summary>
public static class Analytics
{
    /// <summary>
    /// A parameter marker for use inside a question's Where clause. Declared via
    /// <c>WithParameter&lt;T&gt;(name)</c>; bound at ask time from the results door, the ask tools, or
    /// <c>Run(name, parameters)</c>.
    /// </summary>
    public static T P<T>(string name) =>
        throw new InvalidOperationException(
            $"Analytics parameter '{name}' is a marker and is only valid inside a Where clause.");

    /// <summary>The typed surface for one entity — the entry point for fluid asks and named runs.</summary>
    public static AnalyticsSurface<TEntity, TKey> Of<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull => default;

    /// <summary>
    /// The read-model door: materialized rows of a projection, bounded, optionally equality-filtered on
    /// declared columns. Serves by recipe name, so no entity type is required. On-demand questions have
    /// no rows — this refuses and says so.
    /// </summary>
    public static Task<AnalyticsReadModelResult> Rows(
        string name,
        int limit = 100,
        int offset = 0,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken ct = default)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return AnalyticsExecution.ReadRowsAsync(name, limit, offset, filters, ct);
    }

    /// <summary>
    /// The facet door: distinct values of one materialized column with counts. Without
    /// <paramref name="since"/>, the distribution; with it, the movement since that cursor — a
    /// different question, and the envelope says which ran.
    /// </summary>
    public static Task<AnalyticsFacetResult> Facets(
        string name,
        string by,
        string? since = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return AnalyticsExecution.ReadFacetsAsync(name, by, since, limit, ct);
    }

    /// <summary>
    /// The delta door: materialized rows written after a cursor, plus the cursor for the next poll.
    /// Consumers never construct watermarks — pass back what the last response handed over.
    /// </summary>
    public static Task<AnalyticsDeltaResult> Delta(
        string name,
        string? since = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return AnalyticsExecution.ReadDeltaAsync(name, since, limit, ct);
    }

    /// <summary>
    /// A parameter marker for use inside a question's Where clause. At declaration this is a node in the
    /// expression tree; at ask time the bound value is substituted before compilation. Never invoke it
    /// outside an expression — it is a marker, not a method.
    /// </summary>
    public static T Parameter<T>(string name) =>
        throw new InvalidOperationException(
            $"Analytics parameter '{name}' is a marker and is only valid inside a Where clause.");

    /// <summary>
    /// Declare a named question. Declaration is active and startup-visible: names are unique, and the
    /// question joins the shared catalog that endpoints and agents read.
    /// </summary>
    public static AnalyticsQuestion<TEntity, TKey> Question<TEntity, TKey>(
        string name,
        Action<AnalyticsRecipe<TEntity, TKey>> configure,
        int? rowCap = null)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var recipe = new AnalyticsRecipe<TEntity, TKey>();
        configure(recipe);
        AnalyticsQuestion<TEntity, TKey> question = null!;
        question = new AnalyticsQuestion<TEntity, TKey>(
            name.Trim(),
            recipe.RowCap ?? rowCap ?? 0,
            recipe.WhereExpression,
            recipe.ParameterDeclarations,
            recipe.GroupMember,
            recipe.MeasureKind,
            recipe.MeasureMember,
            recipe.Projection,
            recipe.ComputeColumns(),
            (services, q, cap, values, ask, token) =>
                AnalyticsExecution.Run((AnalyticsQuestion<TEntity, TKey>)q, services, cap, values, token, ask),
            (services, values, token) =>
                AnalyticsExecution.Explain<TEntity, TKey>(question, services, values, token),
            (services, trigger, token) =>
                AnalyticsExecution.RefreshAsync<TEntity, TKey>(question, services, token, trigger));
        AnalyticsCatalog.Register(question);
        return question;
    }

    /// <summary>
    /// The shape door: everything about the answer's shape, from the declaration alone — columns,
    /// parameters, bounds, posture. No sink, no compute.
    /// </summary>
    public static AnalyticsShape Shape(string name)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return AnalyticsExecution.Shape(name);
    }

    /// <summary>The explanation door: what this question would do, without executing anything.</summary>
    public static Task<AnalyticsExplanation> Explain(
        string name,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return question.ExplainAsync(
            AppHost.Current ?? throw new InvalidOperationException(
                "No Koan host is active; analytics resolves through the ambient host."),
            parameters, ct);
    }

    /// <summary>The history door: the projection's refresh ledger, newest first.</summary>
    public static Task<AnalyticsHistory> History(
        string name,
        int take = 20,
        CancellationToken ct = default)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared questions: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } declared ? string.Join(", ", declared) : "(none)") + ".");
        }
        return AnalyticsExecution.ReadHistoryAsync(name, take, ct);
    }
}

/// <summary>The per-entity surface: run declared questions, ask ephemeral fluid ones.</summary>
public readonly struct AnalyticsSurface<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Run a declared question by name — the one-line door the whole grammar exists for.</summary>
    public Task<AnalyticsResult> Run(
        string name,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
        => Run(name, parameters, maxAge: null, ct);

    /// <summary>
    /// Run with a per-ask freshness tolerance: a materialization within <paramref name="maxAge"/> is
    /// served, anything older computes live (labeled so). Materialized questions only — the parameter
    /// would lie on an on-demand question, which is always age zero.
    /// </summary>
    public Task<AnalyticsResult> Run(
        string name,
        IReadOnlyDictionary<string, object?>? parameters,
        TimeSpan? maxAge,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            throw new KeyNotFoundException(UnknownQuestion(name));
        }
        if (question is not AnalyticsQuestion<TEntity, TKey> typed)
            throw new KeyNotFoundException(
                $"Analytics question '{name}' is declared for '{question.EntityType.Name}', not " +
                $"'{typeof(TEntity).Name}'. Ask it through that entity's surface or the catalog door.");
        if (maxAge is not null && question.Projection is null)
            throw new NotSupportedException(
                $"Question '{name}' is an on-demand question and computes live on every ask — maxAge " +
                "negotiates the freshness of materializations, so there is nothing for it to do here.");
        return question.ExecuteAsync(
            AppHost.Current ?? throw new InvalidOperationException(
                "No Koan host is active; analytics questions resolve through the ambient host."),
            question.RowCap,
            parameters,
            maxAge is null ? null : new AnalyticsAskOptions { MaxAge = maxAge },
            ct);
    }

    /// <summary>
    /// An ephemeral ask: compose-and-compute now, bounded, without joining the catalog. The same
    /// vocabulary as a declaration — promotion is naming, not translation.
    /// </summary>
    public Task<AnalyticsResult> Ask(
        Action<AnalyticsRecipe<TEntity, TKey>> configure,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var recipe = new AnalyticsRecipe<TEntity, TKey>();
        configure(recipe);
        var question = new AnalyticsQuestion<TEntity, TKey>(
            "(ephemeral)",
            recipe.RowCap ?? 0,
            recipe.WhereExpression,
            recipe.ParameterDeclarations,
            recipe.GroupMember,
            recipe.MeasureKind,
            recipe.MeasureMember,
            recipe.Projection,
            recipe.ComputeColumns(),
            static (services, q, cap, values, ask, token) =>
                AnalyticsExecution.Run((AnalyticsQuestion<TEntity, TKey>)q, services, cap, values, token, ask),
            static (services, values, token) =>
                throw new NotSupportedException("An ephemeral ask is never materialized; there is nothing to explain cold or warm — ask it."),
            static (services, trigger, token) =>
                throw new NotSupportedException("An ephemeral ask is never materialized; there is nothing to refresh."));
        return question.ExecuteAsync(
            AppHost.Current ?? throw new InvalidOperationException(
                "No Koan host is active; analytics questions resolve through the ambient host."),
            question.RowCap,
            null,
            ct);
    }

   /// <summary>The declared names — the same catalog the catalog door and the agents read.</summary>
    public IReadOnlyList<string> Questions => AnalyticsCatalog.Names();

    private static string UnknownQuestion(string name) =>
        $"No analytics question named '{name}' is declared. Declared questions: " +
        (AnalyticsCatalog.Names() is { Count: > 0 } names ? string.Join(", ", names) : "(none)") + ".";
}

/// <summary>
/// Unknown-question telemetry: refusals are loud AND recorded, so the gap between the catalog and what
/// people ask for is visible and can close.
/// </summary>
public static class AnalyticsGapLog
{
    private static readonly AnalyticsRecipeGapLog Log = new();

    public static void Record(string name) => Log.Record(name);

    public static int TotalCount => Log.TotalCount;

    public static IReadOnlyList<(string Name, DateTimeOffset At)> Recent(int take = 20) => Log.Recent(take);

}