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
            recipe.Filter,
            recipe.GroupMember,
            recipe.MeasureKind,
            recipe.MeasureMember,
            recipe.Projection,
            recipe.ComputeColumns(),
            static (services, q, cap, token) =>
                AnalyticsExecution.Run((AnalyticsQuestion<TEntity, TKey>)q, services, cap, token),
            (services, token) =>
                AnalyticsExecution.RefreshAsync<TEntity, TKey>(question, services, token));
        AnalyticsCatalog.Register(question);
        return question;
    }
}

/// <summary>The per-entity surface: run declared questions, ask ephemeral fluid ones.</summary>
public readonly struct AnalyticsSurface<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Run a declared question by name — the one-line door the whole grammar exists for.</summary>
    public Task<AnalyticsResult> Run(string name, CancellationToken ct = default)
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
        return question.ExecuteAsync(
            AppHost.Current ?? throw new InvalidOperationException(
                "No Koan host is active; analytics questions resolve through the ambient host."),
            question.RowCap,
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
            recipe.Filter,
            recipe.GroupMember,
            recipe.MeasureKind,
            recipe.MeasureMember,
            recipe.Projection,
            recipe.ComputeColumns(),
            static (services, q, cap, token) =>
                AnalyticsExecution.Run((AnalyticsQuestion<TEntity, TKey>)q, services, cap, token),
            static (services, token) =>
                throw new NotSupportedException("An ephemeral ask is never materialized; there is nothing to refresh."));
        return question.ExecuteAsync(
            AppHost.Current ?? throw new InvalidOperationException(
                "No Koan host is active; analytics questions resolve through the ambient host."),
            question.RowCap,
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