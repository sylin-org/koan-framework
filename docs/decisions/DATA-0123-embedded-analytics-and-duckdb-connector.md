---
id: DATA-0123
slug: embedded-analytics-and-duckdb-connector
domain: Data
status: Accepted
date: 2026-08-27
title: An embedded analytics pillar, carried first by a DuckDB connector
related:
  - DATA-0119
  - DATA-0120
  - DATA-0122
  - ARCH-0084
  - ARCH-0127
---

# DATA-0123: An embedded analytics pillar, carried first by a DuckDB connector

## Context

Koan's data plane covers transactional reads and writes well (SQLite, JSON, InMemory floors;
server connectors per [DATA-0120](DATA-0120-one-relational-repository-four-drivers.md)) but has
no story for **aggregation-shaped questions** — revenue by month, completion rate by priority,
trend over time. The June 2026 in-process resource survey
([inproc-adapter-survey](../assessment/evidence/inproc-adapter-survey.md)) evaluated DuckDB.NET
as a data adapter and deferred it ("not thin"; "beats sqlite only on analytics-over-large-local"),
and its vector-seam value has since been closed by the shipped InMemory and SqliteVec vector
connectors.

Two research efforts (2026-08-27, preserved as evidence) changed the picture:

- [DuckDB ecosystem research](../assessment/evidence/duckdb-ecosystem-research.md) — adoption is
  accelerating (66.6M PyPI downloads/month, 4th most-admired database, AWS acquiring DuckLabs);
  DuckDB.NET is a mature ADO.NET provider tracking core releases within days; every framework
  in every ecosystem treats DuckDB as a *specialized analytic* adapter, and the vendor's own
  positioning rejects OLTP. The canonical pairing — DuckDB's SQLite extension docs suggest
  "using DuckDB as an analytics engine on top of an existing SQLite application database" — is
  the Koan-shaped architecture. The fastest-growing demand cluster is agentic analytics
  (read-only, row-capped, catalog-driven query surfaces), which is Koan's own positioning.
- [Analytics feature-class satisfaction research](../assessment/evidence/analytics-feature-satisfaction.md)
  — the semantic-layer category's delights (define-once, metrics-as-code, API serving) and
  trust-killers (silent staleness, silent wrong numbers, DSL tax, bottleneck authors, opaque
  refresh) are thoroughly documented. Standalone layers die economically ("horizontal layers
  are hard to sell without owning the application" — Stancil). Declared freshness beat
  always-fresh in the market. Semantic-layer-guarded agent answering scores 98–100% on covered
  questions versus 51–62% unguarded, with loud refusals replacing silent wrong numbers.

## Decision

Two coupled decisions, one building block for the other:

### 1. Adopt a DuckDB data connector — `Sylin.Koan.Data.Connector.DuckDb`

A full SQLite-parity relational adapter riding the existing plane
([DATA-0120](DATA-0120-one-relational-repository-four-drivers.md)): the standard repository
stack, `ILinqSqlDialect` (Postgres-derivative), schema orchestration with an *honest* DDL
policy (create + validate; explicit rebuild where DuckDB cannot ALTER), health contributor,
failure classification (file-lock conflicts classified as ownership/retryable per
[DATA-0122](DATA-0122-adapters-classify-failures-the-framework-decides.md)), and Appender-backed
bulk writes under the existing `write.bulkUpsert`/`write.atomicBatch` tokens.

Honest envelope, declared via capability tokens: everything `SqliteFeatures` declares, plus
`query.fastCount`/`query.optimizedCount` and (where verified) `write.mutationOutcomes`
(`RETURNING`). Not offered: multi-process write sharing of one file; VSS/FTS as transparent
entity indexes; Primary-OLTP-store positioning.

Packaging follows DuckDB.NET's own split: the connector references managed bindings; the native
engine ships as a separate rider package so the floor stays lean. Extension autoinstall is
disabled by default; preloaded extensions and `memory_limit`/`threads`/`temp_directory` are
first-class options.

### 2. Establish the embedded analytics pillar — `Sylin.Koan.Data.Analytics`

A new capability pillar on the data plane whose unit of meaning is the **named recipe** — a
declared, parameterized analytical definition attached to the entity that owns it:

```csharp
public sealed class Todo : Entity<Todo>
{
    static Todo()
    {
        Analytics.Projection("orders-by-month", q => q
            .By(t => t.CreatedAt.Month)
            .Where(t => !t.Done)
            .Sum(t => t.Amount)
            .Refresh(r => r.Cron("0 */6 * * *"))
            .ServeWithin(TimeSpan.FromHours(6)));

        Analytics.Question("completion-rate", q => q
            .By(t => t.Priority)
            .Count());
    }
}
```

Grammar (three declaration forms, one unit of meaning):

- **Vocabulary attributes** on entity properties (`[Dimension]`, `[Measure]`, `[Timestamp]`) —
  mirroring the vector pillar's schema-by-attribute pattern; they generate the self-describing
  catalog that typed queries, REST, and agents all consume.
- **Fluid expressions** (`Todo.Analytics.By(...).Sum(...)`) — ephemeral, bounded asks over the
  best capable engine; promotion to a declared recipe is assignment, not translation.
- **Named recipes** — `Question` (on-demand posture) and `Projection` (materialized posture,
  queryable as a read-only read-model with a generated GET-only controller). Materialization
  rides Koan.Jobs with lease-claimed ownership; triggers are schedule, boot catch-up,
  backfill-on-read, and external trigger (endpoint/`TriggerAsync`); in-process recurrence is
  ledger-driven (self-resubmission) — the empty `Koan.Scheduling` pillar stays unclaimed until
  a second capability needs cron.

The **call-site rule** is normative: *the call site may express intent and nothing else — every
operational decision belongs to declaration or composition, and every operational fact belongs
to the answer.* `Run(name)` returns rows plus a self-describing envelope (recipe, engine, age,
row cap); failures are corrective and list the valid catalog.

Engine election: connectors declare an `analytics.engine` capability; DuckDB is the first and
reference engine. Composition fails loud with a corrective explanation when analytics is
referenced without any engine. The catalog is served read-only to MCP as the agent vocabulary —
agents ask declared, parameterized questions; free-form SQL never enters the surface.

### Research-driven commitments (non-optional parts of the decision)

The feature-class research is normative input, not decoration. The module ships:

1. **Age and provenance on every answer** — silent staleness is the #1 documented trust killer;
   no prior system surfaced answer age by default.
2. **A golden-question conformance harness** — recipes can carry expected-result specs run on
   refresh, extending the capability-tokens-co-defined-with-conformance-checks pattern
   ([ARCH-0084](ARCH-0084-unified-capability-model.md)).
3. **A "request a recipe" loop** — out-of-scope questions refuse loudly and record the gap;
   coverage is managed as a product.
4. **Refresh-state visibility from day one** — last run, duration, skip reasons in facts.
5. **Fail-closed tenant scoping on every consumption path** — queries, exports, drill-downs,
   and MCP tools alike (the documented leak points are the non-query paths).
6. **Determinism** — same question, same answer.
7. **CSV/Parquet export as table stakes.**

## Non-goals and gates

- DuckDB as a **primary transactional store** is not offered or recommended; the connector's
  CRUD surface is a convenience for local analytical datasets.
- VSS (vector) and FTS adapters are explicitly out of scope until upstream persistence
  stabilizes; SqliteVec remains the native in-proc vector path.
- Always-fresh/streaming materialization is not a goal; declared freshness (minutes-to-24h
  band) is where documented demand lives.
- Shared multi-writer DuckDB is gated on Quack/2.0 maturity (fall 2026) — the natural v1.1
  re-evaluation point, alongside the v2.0 storage-format bump handled through the dependency-
  floor machinery.

## Consequences

- The data plane gains its first aggregation-shaped capability and its first new connector
  class since the fleet strategy ([ARCH-0127](ARCH-0127-connector-fleet-strategy.md)): an
  embedded engine making stores applications already operate do more.
- The framework gains three new capability-token families (analytics engine, recipe postures,
  freshness/service level), each co-defined with its conformance check per ARCH-0084.
- Known taxes, accepted and priced: ~105 MB native payload across RIDs (mitigated by split
  packaging); storage-version pinning for shipped files; AOT publishability is unverified and
  gates phase 0; the v2.0 format change is a scheduled future bump.

## Phasing (execution under docs/initiatives/analytics/)

| Phase | Deliverable | Gate |
|---|---|---|
| 0 | DuckDB connector spike — NativeAOT publish probe, floor-RID + extension preload, dialect port, deliberate file-lock conflict through health/doctor classification, ATTACH-SQLite demo | All probe results recorded; AOT verdict stated honestly |
| 1 | Connector v1 — SQLite-parity CRUD, Appender bulk, named-SQL lanes, conformance suite green | AODB conformance cells pass; capacity declared honestly |
| 2 | Analytics v0 — recipe catalog + named questions + `Run` + agent MCP tool over lanes | Call-site rule holds; catalog self-describing; golden-question harness exists |
| 3 | Analytics v1 — projections, materialization, freshness tolerance, read-model query door | Serve-or-compute visible in every answer; refresh state in facts |

Evidence: [duckdb-ecosystem-research](../assessment/evidence/duckdb-ecosystem-research.md) ·
[analytics-feature-satisfaction](../assessment/evidence/analytics-feature-satisfaction.md) ·
supersedes the deferral recorded in the assessment progress ledger
(`X-inproc-data-duckdb`, re-scoped 2026-06-21).
