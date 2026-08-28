# ANL-2 · Analytics module v0 — recipes, Run, catalog, and the agent door

> **Tier**: T3 · **Depends on**: ANL-1 · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. The **call-site rule** and the
> seven honesty commitments in DATA-0123 are acceptance criteria, not flavor. Update
> [../README.md](../README.md) when done.

---

## Why this exists

DATA-0123 phase 2. The connector made aggregation *possible*; this card makes it *Koan*: named,
declared, self-describing questions with one call-site grammar. Everything in this card runs on
the DuckDB engine's **on-demand** posture (compute now, bounded) — materialization and freshness
are ANL-3.

## Mission

Create `src/Koan.Data.Analytics/` (package `Sylin.Koan.Data.Analytics`) plus
`src/Koan.Data.Analytics.Web/` if the HTTP/MCP surfaces need their own leaf. Abstractions that
other engines must see live in the module itself; capability tokens live beside them
(ARCH-0084 pattern) so a reference surfaces them.

### Grammar floor

1. **Capability tokens** — `AnalyticsCaps.Engine` (a connector declares it to become electable),
   `Recipe.Question`, `Recipe.Projection` (reserved; ANL-3 realizes), `Recipe.Run`,
   `Recipe.Catalog`, each co-defined with a conformance check stub.
2. **Recipe declaration** — `Analytics.Question(name, q => q.By(...).Where(...).Count()/Sum()...)`
   from a static initializer on the entity (mirroring the DuckDB connector's own registration
   pattern); a build-time **source-generated catalog manifest** of all recipes (AOT-correct;
   mirror the existing registry-generator manifest approach — do not scan at runtime).
3. **Fluid vocabulary** — `Todo.Analytics.By(...).Where(...).Count()/Sum()/Distribution()`
   executing on-demand over the elected engine, **bounded**: scan caps, row caps, and a
   timeout enforced below the endpoint layer; bounds are answered facts in the envelope, not
   hidden failures.
4. **`Run(name[, args][, fresh])`** — resolves the named recipe, executes over the elected
   engine, returns `AnalyticsResult` with `.Rows` plus the **envelope**: recipe name, engine,
   age (`live` in v0), row cap, parameter binding summary. Unknown name → corrective failure
   listing the catalog (never an empty result).
5. **Engine election** — composition requires exactly one `analytics.engine`; referencing the
   module without an engine fails at startup with a corrective explanation naming
   `Sylin.Koan.Data.Connector.DuckDb` as the reference engine.

### Catalog and doors

6. **Self-describing catalog** — `/analytics/catalog` endpoint + a `facts` section: recipe
   name, posture, dimensions, measures, parameters, bounds. This catalog is the agent
   vocabulary; treat it as a public contract with drift fingerprinting (stamp a fingerprint of
   the declared vocabulary at build time; startup compares — the repairable-drift pattern from
   DATA-0121's successor learning).
7. **Agent MCP tools** — `analytics.list_questions` and `analytics.ask(name, params)`:
   read-only, tenant-routed, row-capped, answers carrying the envelope. Free-form SQL is not
   offered to agents at this rung (that is the documented trust boundary: loud refusals beat
   silent wrong numbers).
8. **Request-a-recipe loop** — unknown-question invocations from any door (API, MCP) are
   recorded (recipe-gap telemetry: the requested intent, timestamp, tenant shape — no payload
   content) and the refusal names the gap. Wire the counter into facts.

### Honesty commitments realized in v0

9. **Golden-question harness** — `AnalyticsQuestionSpec` permitting expected-result assertions
   per recipe (rows, shapes, invariants like "sum of counts == total"), a conformance spec base
   in `tests/`, and recipes declared in the module's own samples carrying specs. The harness
   runs on composition in test hosts and on-demand elsewhere.
10. **Determinism** — same question + same args + same data ⇒ same result ordering (total
    order on rows is part of recipe semantics); spec it.

## Tests

`Koan.Data.Analytics.Tests`: catalog manifest generation (two entities, renames, drift
fingerprint mismatch fails startup correctively), Run semantics (named/unknown/parameterized,
bounds, determinism), election (no engine → startup corrective; two engines → deterministic
election by priority), MCP tools (tenant routing: cross-tenant ask fails closed; row caps
held), harness (a deliberately wrong expected spec fails), request-a-recipe telemetry records
and surfaces.

## Acceptance evidence

- A package-reference-only sample: entity + one declared `Question` + `AddKoan()` →
  `/analytics/catalog` lists it; `Todo.Analytics.Run(name)` returns enveloped rows; MCP tools
  enumerate and ask; a second tenant's ask never sees the first tenant's rows.
- Every envelope field populated in every response; every refusal corrective; zero silent
  failures.
- Build green; new suites green; no regression elsewhere; docs-lint clean.

## STOP rule

If the grammar cannot hold the call-site rule anywhere (a caller forced to say *how*), stop and
record the pressure point — the grammar grows; the rule does not bend.
