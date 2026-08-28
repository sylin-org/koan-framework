---
type: GUIDE
domain: data
title: "Analytics open items ledger"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-28
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-28
  status: verified
  scope: disposition of every open item from the analytics build-out
---

# Analytics open items ledger

Every open item from the build-out, dispositioned. This file is the contract: an item leaves this
page only by being implemented (move to the roster with evidence) or by a recorded decision that
supersedes it.

## Closed this cycle

| Item | Disposition | Evidence |
|---|---|---|
| Linux AOT publish | **Works** — 4.8 MB native linux-x64 binary runs the full probe battery | [Spike evidence §Linux](../../assessment/evidence/duckdb-connector-spike.md) |
| Linux file-lock semantics | **Confirmed** — one writer excludes all other opens (even read-only); conflict error names the holding PID | Spike evidence §Linux |
| Parquet export | **Implemented** — engine-side `COPY` through `IAnalyticsParquetExport`; `?format=parquet` on the rows door; magic-byte tested | AnalyticsControllerSpec |
| Write-surface decision | **Resolved by design** — `AnalyticsController` stands alone (no inherited CRUD verbs); the only mutating route is the fail-closed refresh POST | DATA-0123 + controller docs |
| Parameterized questions (ANL-4 grammar) | **Implemented** — `Analytics.P<T>(name)` marker + `WithParameter<T>`; `AnalyticsParameterBinder` substitutes ask-time values before filter compile (no shared-compiler change was needed — binding happens at the analytics layer); missing/undeclared values refuse before compute; all three doors bind (HTTP query, MCP `parametersJson`, `Run(name, parameters)`) | `AnalyticsParameterSpec`; analytics suite green |
| Facet + delta doors (ANL-5) | **Implemented** — `{recipe}/facets?by=` distribution/movement, `{recipe}/delta?since=` with handed-back `wm1.` cursors, per-row sink stamps, `IAnalyticsChangeTracking` capability, MCP mirrors | `AnalyticsFacetDeltaSpec`; card `cards/analytics-facet-delta-doors.md` |
| Delight doors (ANL-6): explain, history, shape, freshness | **Implemented** — `{recipe}/explain` (serve/compute/refuse + composed SQL + capabilities, never executes), `{recipe}/history` (ledger ring with trigger column), `{recipe}/shape` (declaration-only answer shape), `?maxAge=` freshness negotiation + `MaterializedUtc` envelope + ETag/`Last-Modified`/`Cache-Control: no-cache` with 304 revalidation. Declared defaults (`WithParameterDefault<T>`) let parameterized projections refresh. | `AnalyticsDelightDoorsSpec`; card `cards/analytics-explain-history-shape-freshness.md` |

## Open, with owners and gates

| Item | State | Gate to close |
|---|---|---|
| Window functions in the typed grammar | Not built. Raw-SQL lanes cover them today. | A dialect-expressible window-function shape in the recipe grammar, or explicit permanent v-cut |
| Cron spelling for refresh cadence | `Every(TimeSpan)` ships. Cron arrives with the scheduler pillar (its first consumer may be this module). | Scheduler pillar decision |
| DuckDB 2.0 storage bump | External dependency. Materializations are derived state — the bump is a delete-and-rebuild, not a migration. | Upstream 2.0 release; then bump the floor and re-run the suite |
| Full Koan-host Linux AOT (the app, not the connector spike) | Connector-level Linux AOT verified; the assembled-host publish remains CI work (publish-and-run on linux-x64) | `aot-verify` lane extended to linux-x64 |
| Entity-plane projection rows (future direction) | Alternative storage behind the same doors: materialization rows as `[DataSource("analytics")]` entities written via the Entity plane, replacing the sink's table management. Design recorded in the investigation; not started. | Decide when per-row access manifests or entity-grade storage features are actually needed on projection rows |

## Deliberately not open

- Free-form SQL for agents — refused by design; the catalog is the vocabulary and the coverage boundary.
- Cross-process shared materialization stores — the engine is single-writer per file; per-host derived
  stores that rebuild from the record store are the topology.
- `AnalyticsController` inheriting `EntityController` CRUD — the answer address (`{recipe}`) would
  collide with the record address (`{id}`); the controller stands alone on purpose.
