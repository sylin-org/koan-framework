using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Analytics;
using Koan.Data.Analytics;
using Koan.Data.Analytics.Recipes;
using Koan.Data.Analytics.Runtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Analytics.Web.Controllers;

/// <summary>
/// The analytics surface for one entity. Four doors, one vocabulary:
/// <c>recipes</c> lists what is answerable, <c>{recipe}</c> answers it (serve-or-compute, envelope on
/// every response), <c>{recipe}/rows</c> descends into the materialized rows (CSV-exportable), and
/// <c>{recipe}/refresh</c> is the explicit mutating verb — the only POST in the surface, and the one
/// hosts should gate.
///
/// Deliberately not an <c>EntityController</c>: <c>GET {recipe}</c> occupies the address position the
/// entity's <c>GET &#123;id&#125;</c> would claim, so inheriting the CRUD surface would make every
/// request ambiguous. The entity keeps its own controller; this one speaks answers.
/// </summary>
[ApiController]
public abstract class AnalyticsController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const int MaxResults = 10_000;

    /// <summary>The recipe sheet: every declared question for this entity.</summary>
    [HttpGet("recipes")]
    public IActionResult Recipes()
    {
        var recipes = RecipesForEntity().Select(static question => new
        {
            question.Name,
            Entity = question.EntityType.Name,
            question.MeasureKind,
            MeasureMember = question.MeasureMember,
            GroupMember = question.GroupMember,
            Materialized = question.Projection is not null,
            question.RowCap
        }).ToArray();
        return Ok(new { Entity = typeof(TEntity).Name, Count = recipes.Length, Recipes = recipes });
    }

    /// <summary>
    /// THE door: serve-or-compute per the recipe's policy and return up to
    /// <paramref name="n"/> results with the full envelope — question, engine, age, served-from,
    /// completion. The address never leaks the posture; the answer states it.
    /// </summary>
    [HttpGet("{recipe}")]
    public async Task<IActionResult> Results(string recipe, [FromQuery] int n = 100, CancellationToken ct = default)
    {
        n = Math.Clamp(n, 1, MaxResults);
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        var parameterValues = Request.Query
            .Where(static pair => pair.Key is not ("n" or "format"))
            .ToDictionary(static pair => pair.Key.TrimStart('@', '$'), static pair => (object?)pair.Value.ToString(), StringComparer.Ordinal);
        var answer = await question.ExecuteAsync(HttpContext.RequestServices, n, parameterValues, ct);
        return Ok(answer);
    }

    /// <summary>
    /// The tabular door: materialized rows, paged, equality-filterable on declared columns,
    /// CSV-exportable. Materialized questions only — an on-demand question has no rows, and the door
    /// says so instead of silently computing one.
    /// </summary>
    [HttpGet("{recipe}/rows")]
    public async Task<IActionResult> Rows(string recipe, [FromQuery] int limit = 100, [FromQuery] int offset = 0, [FromQuery] string? format = null, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxResults);
        offset = Math.Max(0, offset);
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        if (question.Projection is null)
            return BadRequest(new { Error = "not-materialized", Message = $"Question '{recipe}' is an on-demand question; run it instead — it has no materialized rows." });

        var sink = HttpContext.RequestServices.GetRequiredService<IAnalyticsProjectionSink>();
        // A never-refreshed projection has no table yet; ensure-then-read answers empty honestly.
        await sink.EnsureAsync(recipe, question.Columns, ct);
        var filters = Request.Query
            .Where(static pair => pair.Key is not ("limit" or "offset" or "format"))
            .Where(pair => question.Columns.Any(column => column.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var rows = await sink.ReadRowsAsync(recipe, limit + 1, offset, filters, ct);

        if (string.Equals(format, "parquet", StringComparison.OrdinalIgnoreCase))
        {
            if (HttpContext.RequestServices.GetRequiredService<IAnalyticsProjectionSink>()
                is not IAnalyticsParquetExport exporter)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    Error = "parquet-not-supported",
                    Message = "The elected engine's materialization sink does not export Parquet."
                });
            var bytes = await exporter.ExportParquetAsync(recipe, filters, ct);
            return File(bytes, "application/octet-stream", $"{recipe}.parquet");
        }

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var columnNames = question.Columns.Select(static column => column.Name).ToArray();
            var lines = new List<string>(rows.Count + 1) { string.Join(',', columnNames) };
            foreach (var row in rows.Take(limit))
                lines.Add(string.Join(',', columnNames.Select(column =>
                {
                    row.TryGetValue(column, out var value);
                    var text = value?.ToString() ?? string.Empty;
                    return text.Contains(',') || text.Contains('"') || text.Contains('\n')
                        ? $"\"{text.Replace("\"", "\"\"")}\""
                        : text;
                })));
            Response.ContentType = "text/csv";
            return Content(string.Join('\n', lines));
        }

        return Ok(new
        {
            Question = recipe,
            Limit = limit,
            Offset = offset,
            Completion = rows.Count > limit ? "RowCapped" : "Complete",
            Rows = rows.Take(limit)
        });
    }

    /// <summary>
    /// The one mutating verb: re-materialize now. For external schedulers (cron, CI) driving freshness
    /// without the in-host loop. Gated by configuration — disabled by default, because an unauthenticated
    /// door that triggers aggregation scans is a load amplifier, not a feature.
    /// </summary>
    [HttpPost("{recipe}/refresh")]
    public async Task<IActionResult> Refresh(string recipe, [FromServices] AnalyticsProjectionRefresher refresher,
        [FromServices] IOptions<AnalyticsOptions> options, CancellationToken ct)
    {
        if (!options.Value.AllowHttpRefreshTrigger)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Error = "refresh-trigger-disabled",
                Message = "The HTTP refresh trigger is disabled (Koan:Data:Analytics:AllowHttpRefreshTrigger). " +
                          "Scheduled refresh, backfill-on-read, and programmatic question.RefreshAsync remain available."
            });

        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        if (question.Projection is null)
            return BadRequest(new { Error = "not-materialized", Message = $"Question '{recipe}' is an on-demand question; it computes live and has nothing to refresh." });

        return Ok(await question.RefreshAsync(HttpContext.RequestServices, ct));
    }

    private bool TryGetQuestion(string recipe, out AnalyticsQuestion question, out IActionResult? notFound)
    {
        question = null!;
        notFound = null;
        if (!AnalyticsCatalog.TryGet(recipe, out question))
        {
            AnalyticsGapLog.Record(recipe);
            notFound = NotFound(new
            {
                Error = "unknown-question",
                Message = $"No analytics question named '{recipe}' is declared for this entity.",
                Recipes = RecipesForEntity().Select(static q => q.Name).ToArray()
            });
            return false;
        }
        if (question.EntityType != typeof(TEntity))
        {
            notFound = NotFound(new
            {
                Error = "wrong-entity",
                Message = $"Question '{recipe}' belongs to '{question.EntityType.Name}', not '{typeof(TEntity).Name}'.",
                Recipes = RecipesForEntity().Select(static q => q.Name).ToArray()
            });
            return false;
        }
        return true;
    }

    private IEnumerable<AnalyticsQuestion> RecipesForEntity() =>
        AnalyticsCatalog.All().Where(static q => q.EntityType == typeof(TEntity));
}
