---
type: GUIDE
domain: data
title: "Entity Analytics How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: verified
  scope: declaration, catalog, run envelope, projections, refresh triggers, read-model door, agent tools
related_guides:
  - ../guides/data/duckdb-engine.md
  - entity-access-and-streaming.md
  - ../recipes/entity-analytics.md
---

# Entity Analytics How-To

Analytics is a declared vocabulary of questions over your entities. You declare a question once —
a measure, an optional grouping, an optional filter, a row cap — and the framework serves it from
code, HTTP, and agent tools, with every answer carrying its own provenance: which store answered it
and how old it is.

Decision authority: [DATA-0123](../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md).
The call-site rule is normative: **the call site expresses intent and nothing else.** Every
operational decision (engine, freshness, bounds) belongs to declaration or composition; every
operational fact (engine, age, cap) belongs to the answer.

## Packages

| Package | Role |
|---|---|
| `Sylin.Koan.Data.Analytics` | The grammar: questions, catalog, execution, envelope. |
| `Sylin.Koan.Data.Analytics.Web` | `/analytics/*` HTTP doors and the agent MCP tools. |
| `Sylin.Koan.Data.Connector.DuckDb` | The elected engine's ADO.NET surface (managed). |
| `Sylin.Koan.Data.Connector.DuckDb.Native` | The engine binary (per-RID). Required to materialize. |

Reference the grammar without an engine and the host refuses at startup with a corrective naming
the engine package. Reference it transitively (through a connector) without declaring questions and
nothing happens.

## Declaration

Declare in the entity's own static initializer, so the vocabulary ships with the type that owns it:

```csharp
public sealed class Todo : Entity<Todo>
{
    static Todo()
    {
        // On-demand: computed where the data lives.
        Analytics.Question<Todo, Guid>("open-count",
            q => q.Where(t => !t.Done).Count());

        // Grouped: one row per group.
        Analytics.Question<Todo, Guid>("open-count-by-priority",
            q => q.Where(t => !t.Done).By(t => t.Priority).Count());

        // Materialized: refreshed, served within tolerance.
        Analytics.Question<Todo, Guid>("done-by-week",
            q => q.Where(t => t.Done).By(t => t.CreatedAt).Count()
                 .Materialize(r => r.Every(TimeSpan.FromHours(6))
                                    .ServeWithin(TimeSpan.FromMinutes(15))
                                    .BackfillOnRead()));
    }
}
```

The grammar: `Where(predicate)` → `By(member)` → one measure — `Count()`, `Sum(m)`, `Min(m)`,
`Max(m)`, `Average(m)` — plus `CapRowsAt(n)`. Only direct property expressions are expressible;
anything else refuses with a corrective naming the member. `Min`/`Max` accept any comparable member;
`Sum`/`Average` require numeric.

## The catalog

Every declared question appears in:

- `AnalyticsCatalog.Names()` / `All()` — in code;
- `GET /analytics/catalog` — over HTTP, including each question's measure, grouping, and bounds;
- `analytics.list_questions` — to agents over MCP.

Names are the contract. They are unique (a duplicate declaration fails startup), deterministic
in ordering, and they are what HTTP routes, MCP tools, and log lines all use.

## Running: the semantic door

```csharp
var answer = await Analytics.Of<Todo, Guid>().Run("open-count-by-priority");
```

The result carries `Rows` — one `AnalyticsRow` per group, `Values` keyed by the member name and the
measure alias (`count`, `sum_Score`, ...) — plus the envelope:

| Field | Meaning |
|---|---|
| `Question` | The declared name that answered. |
| `Engine` | Which store computed or served it. |
| `Age` | `"live"` for on-demand; the materialization's age for served answers. |
| `ServedFrom` | `"live"` or `"materialization"` — the serve-or-compute decision, visible. |
| `RowCap` / `Completion` | The bound, and whether it was reached. |

Determinism is a contract: the same question on the same data returns the same rows in the same
order.

Unknown names throw `KeyNotFoundException` whose message lists the declared catalog — never a
silent empty result — and record the ask in the request-a-recipe loop.

## Fluid asks: the ephemeral door

The same vocabulary runs without a name:

```csharp
var byPriority = await Analytics.Of<Todo, Guid>()
    .Ask(q => q.Where(t => !t.Done).By(t => t.Priority).Count());
```

Ephemeral asks are bounded (row cap + host timeout) and labeled, but never materialize and never
join the catalog. Promotion is naming: wrap the same chain in `Analytics.Question(...)` and it
becomes infrastructure.

## Projections: materialized answers

`Materialize` gives a question a stored answer in the elected engine, refreshed by policy:

| Trigger | What happens |
|---|---|
| `Every(interval)` | The background loop refreshes it when the cadence elapses. |
| Boot catch-up | On host start, anything already due refreshes immediately. |
| Backfill-on-read | A stale `Run` computes live and re-materializes on the way back. |
| External trigger | `POST /analytics/refresh/{name}`, or `question.RefreshAsync(...)` — any external scheduler can drive it. |

`ServeWithin` declares the freshness tolerance: a materialization at most this old is served
(as-is, labeled with its age); anything staler computes live. The serve-or-compute decision and the
reason are in the envelope — silent staleness, the documented #1 trust-killer of this feature
class, is impossible because the answer always says what it is.

The materialization store is a per-host file, `.koan/analytics/Koan.duckdb`. It is derived state:
deleting it loses nothing that a refresh cannot rebuild. Per-host is the topology the engine's
single-writer model wants; per-tenant isolation composes with Database-mode routing (one file per
routed tenant).

## The read-model door

Materialized questions answer tabular reads without computing:

```csharp
// Materialized rows as a queryable read-model — bounded, paged.
var rows = await Analytics.Of<Todo, Guid>().Rows("done-by-week", limit: 100);
```

Over HTTP: `GET /analytics/done-by-week/rows?limit=100&Priority=2` — paging plus equality filters
on declared columns, CSV or Parquet via `?format=csv` / `?format=parquet` (JSON default). On-demand questions have no rows; the
door refuses with `not-materialized` and points at Run.

### Facets: the distribution, and the movement

```csharp
// Distinct values with counts — the filter-dropdown shape.
var facets = await Analytics.Facets("done-by-week", "Week");
var capped = await Analytics.Facets("done-by-week", "Week", limit: 20); // capped answers say so
```

`GET /analytics/done-by-week/facets?by=Week&limit=20`. Facet counts enumerate materialized
tuples; a projection grouped by one column lists its distinct values, one tuple each.

Pass a **watermark** and the question flips from *what is the distribution?* to *what has been
moving since?* — buckets over rows a materialization wrote after the cursor:

```csharp
var movement = await Analytics.Facets("done-by-week", "Week", since: cursor);
// movement.Mode == Movement; movement.ChangesConsidered says what the counts cover;
// movement.DeletesInvisible states the blind spot; movement.Watermark.Current is the next cursor.
```

Honesty notes the envelope carries so they are never implied: a movement answer is not a
distribution (updates count once, at their new value), and deletions are invisible in a derived
store. A malformed watermark refuses with the expected `wm1.<milliseconds>` shape instead of
silently rewinding to the beginning.

### Delta: incremental consumption

```csharp
var page = await Analytics.Delta("done-by-week");                 // first poll: everything + cursor
var next = await Analytics.Delta("done-by-week", since: page.Watermark.Current);  // next poll
```

`GET /analytics/done-by-week/delta?since=wm1.…`. "Changed" means *written by a materialization
after the cursor* — refreshes rewrite wholesale, so every re-materialized row counts as movement.
The response always carries `Watermark: { given, current }`: the consumer holds the cursor, the
door hands back the next one, and the server keeps no per-consumer state.

## Explain, history, shape: the facts without the compute

    var explanation = await Analytics.Explain("done-by-week");   // would it serve, compute, or refuse?
    var ledger      = await Analytics.History("done-by-week");   // refresh ledger, newest first
    var shape       = Analytics.Shape("done-by-week");           // columns, parameters, posture

- **Explain** composes without executing: the elected engine, whether the ask would serve or
  compute (or refuse, with the same corrective execution would raise), the composed SQL, the
  declared vs. supplied parameters, the materialization's age and last-refresh cost, and the
  sink's capabilities (`facets`, `delta`, `parquet`). Side-effect-free by contract: a
  never-refreshed projection still reads as never-refreshed afterwards.
- **History** is the refresh ledger: timestamp, row count, duration, and the trigger:
  `loop`, `http`, `programmatic`, or `backfill-on-read`. "Stale or broken" is one call.
- **Shape** is pure declaration: output columns with CLR types, parameters by name and type,
  bounds, and `Materialized` saying which doors answer. On-demand questions shape too.

## Freshness you can negotiate

`GET /analytics/done-by-week?maxAge=15m` (or `Run(name, parameters, maxAge: ...)`) — durations
parse as `90s` / `15m` / `2h` / `1d` or plain seconds. Within the tolerance the materialization is
served; older computes live, labeled so. A served answer carries `MaterializedUtc`, and the HTTP
door derives `ETag` + `Last-Modified` + `Cache-Control: no-cache` from it, so pollers revalidate
and take 304s: a dashboard's 30-second loop costs nothing when nothing changed. Live answers
carry no caching headers, because they are always fresh and caching them would be a lie.

A parameterized projection refreshes through its declared defaults:
`.WithParameterDefault<int>("min-priority", 0)`. Ask-time values still win; the default is what a
scheduled refresh, which has no ask-time values, binds.

Every path — query, filter, export — passes the same tenant scoping as the semantic door.

## Agents

The catalog is the agent's vocabulary. Four MCP tools, read-only by construction:

- `analytics.list_questions` — the catalog: names, measures, groupings, bounds.
- `analytics.ask(name)` — run a declared question; the answer carries question, engine, age, bounds.
- `analytics.facets(name, by, since?)` — a column's distribution, or movement since a watermark.
- `analytics.delta(name, since?)` — changed rows plus the next watermark, for incremental consumption.

Free-form SQL is not offered to agents: an unanswerable ask returns `unknown-question` with the
catalog, and the ask is recorded so the gap can close as a new declaration. Hallucinated joins and
silent wrong numbers are eliminated *by construction* — the vocabulary is the guardrail.

## The controller door

Analytics doors ride the entity's own controller. Derive from `AnalyticsController<TEntity, TKey>`
and give it a route — the inherited Entity surface (auth seam, `EntityAccess` constraints, WEB-0068
filters, transformers, capability headers, OpenAPI) governs everything, and the analytics doors are
added on top:

```csharp
[Route("analytics/todos")]
public sealed class TodoAnalyticsController : AnalyticsController<Todo, Guid>;
```

That one line exposes, gated exactly like the entity:

| Door | Route | Meaning |
|---|---|---|
| Recipe sheet | `GET analytics/todos/recipes` | Every declared question for this entity — measure, grouping, materialization, bounds. |
| Generic results | `GET analytics/todos/results/{recipe}?n=100` | Run any declared recipe for this entity, bounded at N, full envelope. |

The literal doors win route precedence over the inherited `{id}` parameter deterministically, and the
inherited Entity CRUD surface keeps working underneath — the analytics controller *is* the entity
controller, plus the questions.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Koan:Data:Analytics:RowCap` | `1000` | Default row ceiling for answers. |
| `Koan:Data:Analytics:TimeoutSeconds` | `5` | Wall-clock ceiling for one ask. |
| `Koan:Data:Analytics:MaterializationConnectionString` | `.koan/analytics/Koan.duckdb` | Where the elected engine stores materializations. Per-host by design. |
| `Koan:Data:Analytics:RefreshLoopEnabled` | `true` | The in-host scheduled refresh loop. Disable when an external scheduler drives freshness exclusively, or in tests where each host must own its refresh timing. |
| `Koan:Data:Analytics:AllowHttpRefreshTrigger` | `false` | The HTTP trigger door (`POST /analytics/refresh/{name}`). Fail-closed: enable when the route is gated and an external scheduler should drive freshness. |

## Corrective failures

| Failure | What it means | What to do |
|---|---|---|
| Startup: "no analytics engine is elected" | Questions declared, no engine connector referenced. | Reference `Sylin.Koan.Data.Connector.DuckDb` (+ `.Native`). |
| `unknown-question` | The name is not in the catalog. | Use a listed name, or declare the question. Recorded as a coverage gap. |
| `not-materialized` | The read-model door was asked for an on-demand question's rows. | `Run` it, or add `Materialize` to the declaration. |
| `NotSupportedException: member not expressible` | The member/composition is outside the v0 grammar. | Use a direct property, or a named SQL lane. |
| RowCapped | More groups exist than the cap allowed. | Page the read-model door, or raise the cap on the declaration. |

## Testing

Declare golden questions — known-answer assertions the harness runs for you:

```csharp
AnalyticsGoldenQuestions.Register(new AnalyticsGoldenQuestion
{
    QuestionName = "open-count",
    Assert = answer => Convert.ToInt64(answer.Rows[0].Values["count"]) == 42
        ? null
        : "expected 42 open todos"
});

var failures = await AnalyticsHarness.AuditAsync(host.Services);
failures.Should().BeEmpty();
```

Kept deployments of this feature class ran known-answer checks continuously; killed ones skipped
them. The harness ships in the box so the check is not optional discipline.

## Honest envelope

- On-demand asks scan the record store under bounds; promote hot questions to materializations.
- Only direct property expressions; no window functions in the typed grammar (raw SQL lanes cover
  them).
- Parameters on questions are a v1 grammar item; today a different slice is a second declaration.
- The elected engine is required for the pillar, but v0 computes where the data lives — engine
  acceleration arrives with materializations.
