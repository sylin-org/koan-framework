# ANL-3 · Analytics v1 — projections, freshness tolerance, and the read-model door

> **Tier**: T3 · **Depends on**: ANL-2 · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. The freshness semantics here are
> the module's core bet; the evidence for every choice is in
> [analytics-feature-satisfaction](../../../assessment/evidence/analytics-feature-satisfaction.md)
> (Track 2). Update [../README.md](../README.md) when done.

---

## Why this exists

DATA-0123 phase 3. v0 can *answer* questions; v1 lets entities declare questions whose answers
are **kept** — materialized into the engine, refreshed by declared policy, served within a
declared tolerance, and consumed as queryable read-only read-models. The market verdict the
design rides: declared freshness beat always-fresh; the missing feature everywhere was an
honest age label.

## Mission

Extend `Sylin.Koan.Data.Analytics` with the `Projection` posture.

### Declaration and materialization

1. **`Analytics.Projection(name, q => q... .Refresh(...).ServeWithin(...))`** — a recipe with a
   materialization posture. Materialized rows land in the analytics engine (`.koan/analytics/`
   target), one table per recipe, dimensions forming the natural key + an `AsOf` refresh stamp
   per row; retention declaration (`Retain(grain, olderThan)`) optional.
2. **Refresh as a durable job** — each projection's refresh is a Koan.Jobs work item with
   lease-claimed ownership (`ClaimNext`/`TryRenewLease`), so exactly one host refreshes in a
   farm and the claimer is the only engine-writer by construction. Recurrence is ledger-driven
   (each completion resubmits the next occurrence; coalescing prevents overlap). Do **not**
   claim the empty `Koan.Scheduling` pillar — record it as a candidate extraction when a
   second capability needs cron.
3. **Four triggers, one job:** cron schedule; **catch-up-on-boot** (stale at startup → enqueue
   immediately); **backfill-on-read** (`Run` past tolerance computes live and re-materializes —
   optional, declared); **external trigger** (`POST /analytics/refresh/{recipe}`, admin-gated,
   calling `TriggerAsync`). A failed refresh leaves the previous materialization in place,
   labeled older — staleness, never corruption.

### Serve-or-compute

4. **`Run` honors `ServeWithin`** — materialization younger than tolerance → serve it (age =
   materialization age); stale or cold → compute live (age = `live`), and backfill if declared.
   The decision made is **visible in the envelope on every answer** (`served-from:
   materialization|live`, `age`). This is the trust-killer antidote; no prior system shipped
   it by default.

### The read-model door

5. **`Analytics.Query<TProjection>(...)`** — typed, bounded query over the materialized rows
   (dimensions = natural key; `AsOf` carried per row). Only materialized recipes are queryable
   — `Query<T>` over an on-demand `Question` fails correctively (the line is the posture).
6. **Generated read-only controller** — `GET /analytics/{recipe}` with filter/sort/page over
   declared dimensions and measures, built on the EntityController machinery minus every write
   route. Content negotiation: JSON, **CSV**, **Parquet** (export is table stakes). **Every
   path — query, drill-down, export — passes the same fail-closed tenant scoping** (the
   documented leak points are exactly the non-query paths).
7. **Facts and health** — per-recipe refresh state in `facts` (last run, duration, next run,
   row count, freshness) and `/health/ready` degraded when staleness exceeds tolerance beyond
   a declared grace.

### Capability tokens (realize the v0 stubs)

`Recipe.Projection`, plus a freshness/service-level token carrying the declared tolerance
detail. Conformance checks co-defined: an adapter claiming projection support must pass the
materialization cells below.

## Tests

`Koan.Data.Analytics.Tests` additions: materialization lifecycle (create → refresh → replace;
AsOf advances; retention prunes), serve-or-compute matrix (fresh → serve; stale → compute;
backfill on/off; cold engine → compute), triggers (boot catch-up after simulated downtime;
external trigger endpoint auth; missed-schedule catch-up), farm safety (two hosts, one claim —
lease semantics), read-model door (filter/sort/page correctness; CSV/Parquet shapes; write
verbs absent — 405/404 per the endpoint convention; tenant fail-closed on export), facts and
health projections, determinism across serve and compute paths, golden-question specs still
green after materialization (the harness runs against served *and* live answers).

## Acceptance evidence

- A package-reference-only sample: entity + one `Projection` with `ServeWithin` → first
  `Run` computes live and labels it; after refresh the same `Run` serves with a materialization
  age; killing the refresh job and exceeding tolerance flips the label and (declared) triggers
  backfill — **all three transitions visible in the envelope without touching config**.
- `GET /analytics/{recipe}` serves JSON/CSV/Parquet for the elected tenant only; a second
  tenant receives its own rows or an empty set — never the first tenant's.
- Facts list refresh state per recipe; health degrades per tolerance; golden-question harness
  green across serve and compute.
- Build green; all suites green; docs-lint clean; `docs/reference/capability-map.md` and the
  recipes index carry the analytics entry.

## STOP rule

If serve-or-compute cannot be made visible in the envelope for every path (including exports),
stop — that visibility is the decision's core; shipping materialization without it recreates
the silent-staleness trust-killer the research documents as the category's #1 failure.
