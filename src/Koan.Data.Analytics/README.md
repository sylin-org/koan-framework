# Sylin.Koan.Data.Analytics

Declared analytics for Koan entities: name a question once — a measure, an optional grouping, an
optional filter, a row cap — and it becomes part of the application's shared vocabulary: runnable from
code, listed over HTTP at `/analytics/catalog` (with `Koan.Data.Analytics.Web`), and askable by agents
through the `analytics.list_questions` / `analytics.ask` MCP tools.

## Declare

```csharp
public sealed class Todo : Entity<Todo>
{
    static Todo()
    {
        // On-demand: computed where the data lives, bounded, labeled live.
        Analytics.Question<Todo, Guid>("open-count",
            q => q.Where(t => !t.Done).Count());

        // Materialized: refreshed on a cadence, served within the declared tolerance.
        Analytics.Question<Todo, Guid>("count-by-priority",
            q => q.By(t => t.Priority).Count()
                 .Materialize(r => r.Every(TimeSpan.FromHours(6))
                                    .ServeWithin(TimeSpan.FromMinutes(15))
                                    .BackfillOnRead()));
    }
}
```

## Run

```csharp
var answer = await Analytics.Of<Todo, Guid>().Run("count-by-priority");
answer.Engine;      // which store answered
answer.Age;         // "live", or the materialization's age (e.g. "132s")
answer.ServedFrom;  // "materialization" | "live" — never silent staleness
answer.Completion;  // Complete | RowCapped
answer.Rows;        // the values
```

Unknown names refuse with the catalog — and are recorded in the request-a-recipe loop, so the gap
between what is declared and what is asked becomes visible instead of silent.

## Projections

A materialized question's answer is stored in the elected engine (per-host DuckDB file at
`.koan/analytics/Koan.duckdb`), refreshed by cadence, by boot catch-up, by read-backfill, or through
the trigger door — `POST /analytics/refresh/{name}` and `question.RefreshAsync(...)`. Materialized rows
are served through the read-model door: `GET /analytics/{name}/rows?limit=&offset=&format=csv`, with
equality filters on declared columns. On-demand questions have no rows — the door refuses and points
at Run.

The materialization is per-host by design: the engine is single-writer per file, and a derived store
that rebuilds from the record store wants exactly that topology.

A projection is one table computed for every caller, so it is served and refreshed only where nothing
scopes the caller's reads of the entity. Under soft delete, a data axis, a tenant or a request row filter,
`Run` computes live under the caller's scope (labeled live, never backfilled), and the rows, facets,
delta, history and refresh doors refuse with a corrective ([DATA-0128](../../docs/decisions/DATA-0128-analytics-reads-carry-the-read-scope.md)).

## Honest envelope

- On-demand asks compute **where the data lives** — the entity's relational record store composes the
  aggregate in its own dialect, and the Data boundary ANDs the caller's read scope (segmentation, soft
  delete, data axes, request row filters) into it, so an answer aggregates exactly the rows `Query` returns.
  A store whose composer cannot place the scope refuses.
- Questions are parameter-free in this grammar version; document-expression mapped indexes are
  governed by each store's declared capabilities.
- Only direct property expressions are expressible; everything else refuses with a corrective.
- Cron spelling arrives with the scheduler pillar; refresh cadence is `Every(TimeSpan)` today.

Decision authority: [DATA-0123](../../docs/decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md).

## Install

```powershell
dotnet add package Sylin.Koan.Data.Analytics
```

## What it adds

Declared analytics for Koan entities: named, bounded, self-describing questions with a catalog that humans and agents share. Engine acceleration is elected from a capable connector (DuckDB first, DATA-0123).

## Limits

Configuration decides participation; unsupported requests reject before provider work with a named capability and a correction. Provider-specific limits live in the package's TECHNICAL.md.
