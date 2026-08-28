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
/// The analytics surface for one entity. One vocabulary, six doors:
/// <c>recipes</c> lists what is answerable, <c>{recipe}</c> answers it (serve-or-compute, envelope on
/// every response), <c>{recipe}/rows</c> descends into the materialized rows (CSV-exportable),
/// <c>{recipe}/facets</c> summarizes a column (distribution, or movement since a watermark),
/// <c>{recipe}/delta</c> serves rows a materialization wrote after a cursor, and
/// <c>{recipe}/refresh</c> is the explicit mutating verb — the only POST in the surface, and the one
/// hosts should gate.
///
/// Deliberately not an <c>EntityController</c>: <c>GET &#123;recipe&#125;</c> occupies the address position the
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
    /// THE door: serve-or-compute per the recipe's policy — or the caller's <paramref name="maxAge"/>
    /// tolerance when tighter — and return up to <paramref name="n"/> results with the full envelope.
    /// Materialized serves carry freshness-derived caching headers: an <c>ETag</c> over the answer's
    /// inputs and <c>Cache-Control: no-cache</c>, so a polling dashboard revalidates cheaply and takes
    /// 304s. The address never leaks the posture; the answer states it.
    /// </summary>
    [HttpGet("{recipe}")]
    public async Task<IActionResult> Results(string recipe, [FromQuery] int n = 100, [FromQuery] string? maxAge = null, CancellationToken ct = default)
    {
        n = Math.Clamp(n, 1, MaxResults);
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;

        TimeSpan? tolerance = null;
        if (maxAge is not null)
        {
            if (!AnalyticsDuration.TryParse(maxAge, out var parsed) || parsed < TimeSpan.Zero)
                return BadRequest(new
                {
                    Error = "invalid-max-age",
                    Message = $"'{maxAge}' is not a duration. Spell it as 90s / 15m / 2h / 1d, or plain seconds."
                });
            if (question.Projection is null)
                return BadRequest(new
                {
                    Error = "max-age-on-demand",
                    Message = $"Question '{recipe}' is an on-demand question and computes live on every ask — maxAge negotiates the freshness of materializations."
                });
            tolerance = parsed;
        }

        var parameterValues = Request.Query
            .Where(static pair => pair.Key is not ("n" or "format" or "maxAge"))
            .ToDictionary(static pair => pair.Key.TrimStart('@', '$'), static pair => (object?)pair.Value.ToString(), StringComparer.Ordinal);
        var answer = await question.ExecuteAsync(
            HttpContext.RequestServices, n, parameterValues,
            tolerance is null ? null : new AnalyticsAskOptions { MaxAge = tolerance }, ct);

        if (answer.ServedFrom == "materialization" && answer.MaterializedUtc is { } materializedUtc)
        {
            var etag = $"\"{EtagOf(recipe, n, parameterValues, materializedUtc, tolerance ?? question.Projection?.ServeWithin)}\"";
            Response.Headers.LastModified = materializedUtc.ToString("R");
            Response.Headers.ETag = etag;
            // no-cache (revalidate-every-time) over no-store: a 304 still answers "is it fresh" without
            // shipping rows, and a cached body can never outlive its materialization.
            Response.Headers.CacheControl = "no-cache";
            if (Request.Headers.IfNoneMatch.Count > 0 &&
                Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
                return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(answer);
    }

    /// <summary>A stable digest of everything the answer depends on — the materialization stamp makes it change exactly when the data does.</summary>
    private static string EtagOf(string recipe, int n, Dictionary<string, object?> parameters, DateTimeOffset materializedUtc, TimeSpan? tolerance)
    {
        var inputs = string.Join('|', recipe, n,
            string.Join(';', parameters.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => $"{p.Key}={p.Value}")),
            materializedUtc.UtcTicks, tolerance?.Ticks ?? 0);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inputs));
        return Convert.ToHexString(bytes)[..32];
    }

    /// <summary>
    /// The explanation door: what this question would do — serve, compute, or refuse — with the
    /// composed SQL, bounds, parameters, and the sink's capabilities. Never executes anything: a
    /// never-refreshed projection still reads as never-refreshed afterwards.
    /// </summary>
    [HttpGet("{recipe}/explain")]
    public async Task<IActionResult> Explain(string recipe, CancellationToken ct = default)
    {
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        var parameterValues = Request.Query
            .Where(static pair => pair.Key is not ("n" or "format" or "maxAge"))
            .ToDictionary(static pair => pair.Key.TrimStart('@', '$'), static pair => (object?)pair.Value.ToString(), StringComparer.Ordinal);
        return Ok(await question.ExplainAsync(HttpContext.RequestServices, parameterValues, ct));
    }

    /// <summary>
    /// The history door: the projection's refresh ledger, newest first — when, how many rows, how
    /// long, and what triggered it. "Stale or broken" is one call; on-demand questions have nothing
    /// to look back on and refuse.
    /// </summary>
    [HttpGet("{recipe}/history")]
    public async Task<IActionResult> History(string recipe, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        if (question.Projection is null)
            return BadRequest(new { Error = "not-materialized", Message = $"Question '{recipe}' is an on-demand question; it has never refreshed and never will — history reads materializations." });
        try
        {
            return Ok(await Analytics.History(recipe, take, ct));
        }
        catch (NotSupportedException refusal)
        {
            return BadRequest(new { Error = "history-refused", Message = refusal.Message });
        }
    }

    /// <summary>
    /// The shape door: the answer's columns, parameters, bounds, and posture — from the declaration
    /// alone. Works for on-demand questions too, with Materialized false saying which doors apply.
    /// </summary>
    [HttpGet("{recipe}/shape")]
    public IActionResult Shape(string recipe)
    {
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        return Ok(Analytics.Shape(question.Name));
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
    /// The facet door: distinct values of one materialized column with counts — filter dropdowns
    /// without declaring a recipe per facet. Without <paramref name="since"/>, the distribution;
    /// with it, the movement since that watermark, and the envelope names which question ran.
    /// </summary>
    [HttpGet("{recipe}/facets")]
    public async Task<IActionResult> Facets(string recipe, [FromQuery] string? by = null, [FromQuery] string? since = null,
        [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(by))
            return BadRequest(new
            {
                Error = "facet-column-required",
                Message = "The facets door needs ?by=column — a declared column of the materialization."
            });
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        if (question.Projection is null)
            return BadRequest(new { Error = "not-materialized", Message = $"Question '{recipe}' is an on-demand question; facets read materializations — run it instead." });

        try
        {
            return Ok(await Analytics.Facets(recipe, by, since, limit, ct));
        }
        catch (NotSupportedException refusal)
        {
            return BadRequest(new { Error = "facets-refused", Message = refusal.Message });
        }
    }

    /// <summary>
    /// The delta door: materialized rows written after a watermark, plus the next watermark on every
    /// response — the consumer holds the cursor, the door hands it back, the server keeps no
    /// per-consumer state.
    /// </summary>
    [HttpGet("{recipe}/delta")]
    public async Task<IActionResult> Delta(string recipe, [FromQuery] string? since = null,
        [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!TryGetQuestion(recipe, out var question, out var notFound)) return notFound;
        if (question.Projection is null)
            return BadRequest(new { Error = "not-materialized", Message = $"Question '{recipe}' is an on-demand question; deltas read materializations — run it instead." });

        try
        {
            return Ok(await Analytics.Delta(recipe, since, limit, ct));
        }
        catch (NotSupportedException refusal)
        {
            return BadRequest(new { Error = "delta-refused", Message = refusal.Message });
        }
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

        return Ok(await question.RefreshAsync(HttpContext.RequestServices, ct, "http"));
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
