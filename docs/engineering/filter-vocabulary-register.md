---
type: ENGINEERING
domain: data
title: "Data filter vocabulary register"
audience: [maintainers, framework-authors, ai-agents]
status: current
last_updated: 2026-08-29
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-29
  status: verified
  scope: entries verified against this tree by the sessions that recorded them; each entry carries
    its own evidence. Created 2026-08-29 following the media-shaping-register / connector-fleet
    LEDGER conventions because no data-axis work register existed for non-initiative framework
    sessions; relocating it is an owner call.
---

# Data filter vocabulary register

Dated record of changes to the closed filter vocabulary (`FilterOperator`, the `$` DSL, the LINQ
lift, and per-adapter pushdown declarations), so a future session does not re-litigate what is
already addressed. One entry per change; append, never rewrite.

## 1. Collection-element substring (`$like` → `HasContains`) — SHIPPED — 2026-08-29

**What.** One new operator on the collection side: some string element contains a substring. DSL
keyword `$like`, valid only on a collection leaf (scalar leaf throws `FilterParseException` naming
`$contains` as the corrective). `$contains` is unchanged on both leaf kinds. The LINQ lift
`collection.Any(i => i.Contains(closedString))` produces the same node; any other `Any` body stays a
`ClrFilter` residual. Both evaluators (CLR + schemaless dictionary oracle) evaluate `HasContains` as
case-sensitive ordinal substring over string elements; null, missing, and empty arrays match nothing.

**Lowering per adapter.**

| Adapter | Push | Lowering |
|---|---|---|
| Sqlite | yes | `json_each` + `instr(element, literal)` — SQLite's LIKE folds ASCII case, so the exact `instr` carries the case-sensitive posture |
| Postgres / Cockroach | yes | `jsonb_array_elements_text` + `LIKE %pat% ESCAPE '\'` (natively case-sensitive) |
| SqlServer | yes | `OPENJSON(ISNULL(...))` + `value COLLATE Latin1_General_BIN2 LIKE` (default collation folds case; the binary collation states the posture) |
| MySql | yes | `JSON_SEARCH(col,'one',pat) IS NOT NULL` (JSON comparisons ride the binary collation; verified by the MySql convergence suite) |
| DuckDb | yes | `json_each` + `(value ->> '$') LIKE` |
| Mongo | yes | `{ path: { $elemMatch: { $regex: <escaped literal> } } }` |
| Couchbase | yes | `ANY x IN path SATISFIES x LIKE %pat% END` (N1QL default backslash escape) |
| ElasticSearch / OpenSearch | yes (vector `VectorCaps.Filters`) | case-sensitive `wildcard` over the projected value token, wildcard metacharacters escaped |
| Redis | yes (scan) | evaluates the AST through the in-memory evaluators; `FilterSupport.Full` includes the new operator automatically and stays honest |
| InMemory / Json | yes (scan, KeyValue family) | `KeyValueStore` declares `FilterSupport.Full` and evaluates the AST through `InMemoryFilterEvaluator`/`KvFilterEvaluator`, so the operator is served by the same honest floor |
| Vector adapters (Milvus, PgVector, Qdrant, RedisVector, Weaviate, MongoAtlasVector) | absent | no faithful element-substring lowering exists there; refusal is loud (vector path hard-errors on residual) |

**Evidence.** Commit `f74f30f12` + this register's commit. Suites executed on the recording machine:
Filtering 107/107, Sqlite 49/49, InMemory 56/56, Json 41/41, Relational 26/26, Data.Core 492/492,
plus the container convergence suites recorded in the task report. The shared TestKit `$like`
battery (8 cases over a corpus with LIKE metacharacters and null arrays) runs inside every
convergence suite with posture-aware receipts: an adapter that declares the operator must show no
fallback fact; one that does not must record the residual.

**Owner calls left open.**
1. `$like` is a working name. Alternatives the owner may prefer: `$hasContains` (matches the enum),
   `$elemMatch`-style explicitness, or reserving `$like` for a future wildcard/pattern operator.
2. The vocabulary expansion itself may want an ADR (the closed vocabulary is DATA-0096's table;
   this entry amends it in behavior, not in the ADR — ADRs stay untouched by design here).
3. The pinned posture "store-side case-sensitive, consistent with `Contains`" is only partially true
   of the existing tree: the scalar `Contains`/`StartsWith`/`EndsWith` path emits store `LIKE`, which
   folds ASCII case on SQLite and folds case under SqlServer/MySQL default collations. The new
   operator states case sensitivity explicitly per dialect; the scalar path's latent divergence was
   left untouched (out of scope) and is recorded here as a candidate owner decision.
4. `VectorFilterReader` (the schemaless DSL twin) does not accept `$like` yet; its explicit intent
   keywords (`$has`, `$hasAny`, …) would take `$like` naturally the day a vector adapter can honor
   it. Added then, not now.
