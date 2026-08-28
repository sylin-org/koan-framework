---
type: GUIDE
domain: framework
title: "Embedded analytics initiative"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: phase plan, card roster, and gates for the DuckDB connector and analytics pillar
---

# Embedded analytics initiative

**Mission.** Give Koan's data plane an aggregation-shaped capability: entities that can be
*asked questions* — declaratively, cheaply, honestly, and safely by humans and agents alike.

**Normative decision:** [DATA-0123](../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md).
Read it first; this file is the execution surface and deliberately restates none of its
reasoning. **Evidence:**
[duckdb-ecosystem-research](../../assessment/evidence/duckdb-ecosystem-research.md) ·
[analytics-feature-satisfaction](../../assessment/evidence/analytics-feature-satisfaction.md).

## The two rules the implementation must not break

1. **The call-site rule (DATA-0123):** the call site expresses intent and nothing else. Every
   operational decision belongs to declaration or composition; every operational fact belongs
   to the answer. If a caller needs to say *how*, the grammar is missing a declaration.
2. **The honesty commitments (DATA-0123 §research-driven):** age + provenance on every answer;
   golden-question conformance harness; request-a-recipe loop; refresh state visible from day
   one; fail-closed tenant scoping on every consumption path; determinism; CSV/Parquet export.
   These are scope, not aspiration — a phase that skips one is not done.

## Phases and gates

| Phase | Card | Deliverable | Gate (all must hold to advance) |
|---|---|---|---|
| 0 | [cards/duckdb-connector-spike.md](cards/duckdb-connector-spike.md) | Probes: NativeAOT publish, RID/native coverage, extension preload (air-gap), dialect smoke, file-lock + conflict classification, ATTACH-SQLite demo | Every probe verdict recorded in the spike notes; AOT verdict stated honestly (works / works-with-curation / broken); go/no-go for phase 1 |
| 1 | [cards/duckdb-connector-v1.md](cards/duckdb-connector-v1.md) | `Sylin.Koan.Data.Connector.DuckDb` — SQLite-parity CRUD, Appender bulk, named-SQL lanes, health/doctor, honest schema policy | AODB conformance cells green; capabilities declared exactly; connector README/TECHNICAL; dependency floors stamped |
| 2 | [cards/analytics-module-v0.md](cards/analytics-module-v0.md) | `Sylin.Koan.Data.Analytics` v0 — recipe catalog, `Question` posture, `Run`, answer envelope, agent MCP tools, request-a-recipe loop, golden-question harness | Call-site rule holds in every public surface; catalog self-describing at `/analytics/catalog` + facts; harness runs recipes on refresh |
| 3 | [cards/analytics-module-v1-projections.md](cards/analytics-module-v1-projections.md) | `Projection` posture — materialization, four refresh triggers, `ServeWithin` serve-or-compute, read-model query door, retention | Serve-or-compute decision visible in every answer; refresh state in facts/health; export paths tenant-fail-closed |

## Roster (ledger)

| ID | Card | Tier | Depends on | Status | Notes |
|---|---|---|---|---|---|
| ANL-0 | [cards/duckdb-connector-spike.md](cards/duckdb-connector-spike.md) | T3 | — | **done** (GO) | 2026-08-27 — evidence: [duckdb-connector-spike.md](../../assessment/evidence/duckdb-connector-spike.md). AOT works (win-x64, 4.4MB); floor RIDs covered; ATTACH-SQLite works; positional params only; one-process-per-file (even RO excluded while held) |
| ANL-1 | [cards/duckdb-connector-v1.md](cards/duckdb-connector-v1.md) | T3 | ANL-0 | **done** | 2026-08-27 — `Sylin.Koan.Data.Connector.DuckDb` + `.Native`; suite 49/49 green (AODB all modes, convergence, paging, lanes, redaction); SQLite sibling suite 49/49 green after the shared bare-name parameter fix |
| ANL-2 | [cards/analytics-module-v0.md](cards/analytics-module-v0.md) | T3 | ANL-1 | **done** | 2026-08-27 — `Sylin.Koan.Data.Analytics` (+`.Web`): catalog, `Run` envelope, election gate (usage-keyed), MCP tools, gap log, golden harness; analytics suite 10/10; DuckDb 49/49; SQLite 49/49. v0 deviations (declared): questions parameter-free, projection token reserved for ANL-3, MCP ask JSON-params deferred |
| ANL-3 | [cards/analytics-module-v1-projections.md](cards/analytics-module-v1-projections.md) | T3 | ANL-2 | **done** | 2026-08-27 — projections: per-host DuckDB materialization store, `Every` cadence + boot catch-up + backfill-on-read + trigger door, serve-or-compute with `ServedFrom` on every answer, read-model door (`rows` + CSV), refresh state in facts; analytics suite 14/14; DuckDb 49/49; SQLite 49/49. Declared v-cuts: `Every(TimeSpan)` not cron (scheduler pillar), CSV not Parquet, equality filters only |
| ANL-4 | (grammar item from [OPEN-ITEMS.md](OPEN-ITEMS.md), no card) | T3 | ANL-2 | **done** | 2026-08-28 — parameterized questions: `Analytics.P<T>(name)` marker + `WithParameter<T>`, `AnalyticsParameterBinder` binds at ask time before filter compile (shared seam: DuckDb + Sqlite composers); undeclared/missing values refuse before compute; results door, `analytics.ask`, and `Run(name, parameters)` all bind. Parquet export also landed here (engine-side COPY, `?format=parquet`). Evidence: `AnalyticsParameterSpec`; analytics suite 22/22 |
| ANL-5 | [cards/analytics-facet-delta-doors.md](cards/analytics-facet-delta-doors.md) | T3 | ANL-3 | **done** | 2026-08-28 — facet + delta doors: `{recipe}/facets?by=` (distribution, or movement since a watermark), `{recipe}/delta?since=` (changed rows + handed-back cursor, `wm1.` codec, opaque to consumers), per-row `_koan_stamp` in the sink (back-fit via ALTER), `IAnalyticsChangeTracking` capability, MCP `analytics.facets`/`analytics.delta`; envelope states mode, `ChangesConsidered`, and `DeletesInvisible`. Evidence: `AnalyticsFacetDeltaSpec`; analytics suite 28/28 |
| ANL-6 | [cards/analytics-explain-history-shape-freshness.md](cards/analytics-explain-history-shape-freshness.md) | T3 | ANL-3, ANL-5 | **done** | 2026-08-28 — delight doors: `explain` (serve/compute/refuse + composed SQL + sink capabilities, side-effect-free), `history` (per-projection ledger ring, trigger column: loop/http/programmatic/backfill-on-read), `shape` (declaration-only columns/parameters/posture), freshness negotiation (`?maxAge=`, per-ask tolerance over `ServeWithin`, `MaterializedUtc` on the envelope, ETag + `Cache-Control: no-cache` + 304 revalidation on the results door); `WithParameterDefault<T>` lets parameterized projections refresh. MCP mirrors: `analytics.explain/history/shape`. Sink: schema ensured once per recipe per instance (pooled-connection catalog races) |

Agents: open your card, paste it into a fresh session, record your verdicts in the card's named
evidence location, and update your row here — `in-progress` when you start, `done` / `blocked`
with a one-line note when you finish. If repo reality contradicts the card, record the
divergence in your row rather than silently re-scoping.

## Explicit non-goals (DATA-0123)

DuckDB as a primary transactional store; VSS/FTS adapters until upstream stabilizes; always-
fresh/streaming materialization; shared multi-writer files (gated on Quack/2.0 — the natural
v1.1 re-evaluation point, together with the v2.0 storage-format bump via the dependency-floor
machinery).
