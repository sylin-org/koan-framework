---
type: RECIPE
recipe: entity-analytics
title: "Ask analytics questions about my own entities"
domain: data
status: current
last_updated: 2026-08-27
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: verified
  scope: docs/recipes/entity-analytics.md
gets_you: "Declared, named questions over your entities — runnable from code, listed over HTTP, and askable by agents — where every answer says which store produced it and how old it is."
works_if: "Entities live on a relational store (SQLite on the local path), and the questions are aggregations — counts, sums, min/max/average — optionally grouped by a property."
costs: "On-demand asks are free to operate; they run against the store that owns the data. Materialized projections add a per-host engine file under .koan/analytics/ that rebuilds from the record store, plus a background refresh loop."
ingredients:
  - "one | declared questions, catalog, and the analytics grammar | Sylin.Koan.Data.Analytics"
  - "one | HTTP catalog, read-model door, and agent tools | Sylin.Koan.Data.Analytics.Web"
  - "one | elected analytics engine (materialization store) | Sylin.Koan.Data.Connector.DuckDb + Sylin.Koan.Data.Connector.DuckDb.Native"
  - "one | the record store the entities live on | Sylin.Koan.Data.Connector.Sqlite"
---

# Ask analytics questions about my own entities

Dashboards, "how many", "how much", "grouped by what" — these are aggregation questions, and they
are different from Entity reads: bounded, named once, and honest about how old the answer is. This
recipe declares them over entities you already have, serves them everywhere, and lets agents ask
them without ever handing one a SQL string.

## When this is the answer

- "Revenue by month." "How many open orders per region?" "What's the average basket?"
- People or agents ask the same aggregations repeatedly, and the numbers must mean the same thing
  every time.
- You want answers served from a fast local engine without standing up a warehouse.

**When it is not:** one-off exploratory queries during development (open the store's file and query
it — the framework is not in the way); genuinely ad-hoc free-form queries from end users (no
governed vocabulary exists for that here, by design).

## Assembly

Reference the grammar, the web surface, an engine, and the record store:

```powershell
dotnet add package Sylin.Koan.Data.Analytics
dotnet add package Sylin.Koan.Data.Analytics.Web
dotnet add package Sylin.Koan.Data.Connector.DuckDb
dotnet add package Sylin.Koan.Data.Connector.DuckDb.Native
dotnet add package Sylin.Koan.Data.Connector.Sqlite   # the record store (usually already present)
```

Declare questions where the entity lives:

```csharp
public sealed class Todo : Entity<Todo>
{
    static Todo()
    {
        Analytics.Question<Todo, Guid>("open-count",
            q => q.Where(t => !t.Done).Count());

        Analytics.Question<Todo, Guid>("open-count-by-priority",
            q => q.Where(t => !t.Done).By(t => t.Priority).Count());
    }
}
```

Run them:

```csharp
var answer = await Analytics.Of<Todo, Guid>().Run("open-count-by-priority");
foreach (var row in answer.Rows)
    Console.WriteLine($"{row.Values["Priority"]}: {row.Values["count"]}");
```

The host refuses to start if questions are declared but no engine is elected — the corrective names
`Sylin.Koan.Data.Connector.DuckDb`, which is what the reference above elects.

## What the guarantee is

- **Determinism**: the same question on the same data returns the same rows in the same order.
- **Provenance on every answer**: question name, engine, age, row cap — as fields, not log lines.
- **Loud refusals**: an unknown name lists the catalog; an unexpressible member names itself. Nothing
  fails with a plausible-looking wrong number.
- **Agents never touch SQL**: they enumerate the catalog and ask declared questions.

## Serve it everywhere

- Code: `Analytics.Of<Todo, Guid>().Run("open-count-by-priority")`.
- HTTP: `GET /analytics/catalog` and `GET /analytics/run/open-count-by-priority`.
- Agents (MCP): `analytics.list_questions` and `analytics.ask(name)`.
- The entity's own controller: derive `AnalyticsController<Todo, Guid>` with a route and get
  `GET .../recipes` (the recipe sheet) plus `GET .../results/{recipe}?n=100` (any recipe, bounded) —
  governed by the same auth and access machinery as the entity.
- CSV: `GET /analytics/run/...` answers and materialized rows export via `?format=csv` on the
  read-model door.

## Materialize (optional)

An on-demand question computes where the data lives. `Materialize` promotes it: the answer is stored
in the elected engine, refreshed on a cadence (or by trigger), and served within the tolerance you
declare — with the serve-or-compute decision visible on every answer.

```csharp
Analytics.Question<Todo, Guid>("done-by-week", q => q
    .Where(t => t.Done)
    .By(t => t.Priority)
    .Count()
    .Materialize(r => r.Every(TimeSpan.FromHours(6))
                       .ServeWithin(TimeSpan.FromMinutes(15))
                       .BackfillOnRead()));
```

- **Every** — the scheduled refresh cadence.
- **ServeWithin** — answers at most this old are served from the engine; staler ones compute live.
- **BackfillOnRead** — a stale read also re-materializes, so the engine self-heals.

Materialized rows read back through the read-model door: `GET /analytics/{name}/rows?limit=100`,
with CSV export via `?format=csv` and equality filters on declared columns.

## Costs and limits

- On-demand asks compute over the record store — bounded by the question's row cap and the host
  timeout, but they are still scans. Promote hot questions to materializations.
- The materialization store is a **per-host** engine file (`.koan/analytics/Koan.duckdb`). It
  rebuilds from the record store, so per-host is the topology, not a limitation.
- The refresh loop is an in-host background service. Cron spelling arrives with the scheduler pillar;
  today's cadence is `Every(TimeSpan)`, and any external scheduler can drive
  `POST /analytics/refresh/{name}`.
- Questions are parameter-free in this grammar version. A question that needs a different slice is a
  second declared question, not an argument.

## Full manual

[Entity analytics how-to](../guides/data/entity-analytics.md) — declaration, fluid asks, the
envelope, projections, triggers, the read-model door, and the agent surface.

[Open the DuckDB engine guide](../guides/data/duckdb-engine.md) — the elected engine's install split,
single-writer posture, engine options, and materialization store.
