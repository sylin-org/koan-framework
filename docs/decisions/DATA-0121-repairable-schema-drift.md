---
id: DATA-0121
slug: repairable-schema-drift
domain: Data
status: Accepted
date: 2026-08-22
title: Which schema drift the framework repairs
related:
  - DATA-0119
  - DATA-0120
---

# DATA-0121: Which schema drift the framework repairs

## Context

`RelationalSchemaOrchestrator.EnsureCreatedAsync` added columns a table was missing and treated every other
difference as something to report or refuse. Nothing was ever corrected.

That left a real gap. A projected column — one the store computes from the structured document, MySQL's
`GENERATED ALWAYS AS ... STORED`, SQL Server's `AS ... PERSISTED` — can go stale without its type changing at
all. The dialect changes how it reads a JSON scalar, new tables get the new expression, and a table an earlier
Koan created keeps the old one. Neither the column's type nor its nullability says so.

The consequences are not cosmetic. Before the JSON-null read was fixed, the old expression is what made a null
write fail. And a store's optimizer only substitutes a computed column for a query that spells the value
identically, so a stale column quietly stops serving every index built on it. An upgraded database reported
`Degraded` forever, and the only remedy was a human writing the `ALTER` by hand.

The reason it had not been fixed is the risk. "Add a column that is missing" and "replace a column that exists"
are different acts: one is additive, the other can destroy data if the framework is wrong about what it is
looking at.

## Decision

**A projected column, and only a projected column, is repaired — under the consent that would have created
it.**

- A projected column holds no value of its own. The store recomputes it from the structured document on every
  write, so rebuilding one loses nothing, whatever it had drifted into. That property, not the kind of drift,
  is what makes it repairable: any drift on a projected column is corrected by re-emitting its definition.
- Every other column holds its own value and is refused, exactly as before. The structured root is the sharpest
  case and is pinned by a spec: drift there stops the mapping rather than rebuilding it.
- Repair rides `IsDdlAllowed` — the same gate that lets Koan create the column — rather than earning a second
  knob. A store that may not issue DDL still receives the finding, and its reads still resolve through the
  document.
- Repair precedes index creation, so an index is never built over an expression that is about to change.
  MySQL's `MODIFY COLUMN` rebuilds the indexes over the column it restates, which is what brings back the
  indexes the stale expression had retired.
- `IRelationalDdlExecutor.RebuildProjection` is the one contract addition, defaulting to a refusal the way
  `CreateIndex` does: the orchestrator asks only a store that answered `SupportsPersistedComputedColumns`, and
  a store that computes a column it cannot restate is a contradiction worth hearing about.

## Consequences

- An upgraded database repairs itself on the boot that notices, and validates clean afterwards. The MySQL spec
  asserts the column's recipe marker goes from empty to `koan-gen:` — a marker only the `ALTER` writes — so the
  report cannot pass by validation merely giving up.
- **The boundary must not widen casually.** "Derived data can be recomputed" is the whole justification; a
  repair path that lost that distinction would be a data-loss defect waiting for its first wrong
  classification.
- Only MySQL and SQL Server project at all, and **only MySQL can currently detect that a projection is
  stale** — SQL Server describes its columns by presence only and compares nothing (PMC-054). Repair landing
  first is deliberate: giving SQL Server the marker afterwards means the first upgraded boot fixes what the
  marker finds instead of reporting it forever.
- Strict matching still refuses rather than repairs. A caller who asked to be told about every difference is
  told.

## References

- `src/Koan.Data.Relational/Orchestration/RelationalSchemaOrchestrator.cs` — `RepairableProjections`
- `src/Koan.Data.Relational.Abstractions/Orchestration/IRelationalDdlExecutor.cs` — `RebuildProjection`
- `tests/Suites/Data/Relational/.../RelationalOwnershipSpec.cs` — rebuilt under `AutoCreate`, reported under
  `NoDdl`, refused for the structured root
- `docs/initiatives/koan-v1/POST-CYCLE-TODO.md` — PMC-045, PMC-052, PMC-054
